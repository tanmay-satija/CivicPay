using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CivicPay.Application;
using CivicPay.Domain;
using CivicPay.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicPay.IntegrationTests;

public class AuditRegressionTests
{
    private static PaymentRequest Payment(decimal amount = 25) => new("DEMO-COUNTY", "PROPERTYTAX-0001", "PropertyTax", amount, DateOnly.FromDateTime(DateTime.UtcNow), "AUDIT-RACE");
    private static DbContextOptions<CivicPayDbContext> Options(TestApp app, IInterceptor? interceptor = null)
    {
        var builder = new DbContextOptionsBuilder<CivicPayDbContext>().UseSqlite($"Data Source={app.DatabasePath};Foreign Keys=True;Default Timeout=30");
        if (interceptor != null)
            builder.AddInterceptors(interceptor);
        return builder.Options;
    }

    private sealed class UnseededApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Database:Seed", "false");
        }
    }

    [Fact]
    public async Task Fresh_database_without_demo_data_has_usable_catalogue()
    {
        using var app = new UnseededApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/municipalities", new ConfigurationRequest("AUDIT", "Audit", "CAD", true, 5, ["Utility"]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(["Utility"], (await response.Content.ReadFromJsonAsync<ConfigurationResponse>())!.AcceptedPaymentTypes);
        using var scope = app.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CivicPayDbContext>().Accounts.CountAsync());
    }

    private sealed class BeforeAccountQuery(Func<Task> action) : DbCommandInterceptor
    {
        private bool invoked;
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken ct = default)
        {
            if (!invoked && command.CommandText.Contains("FROM \"Accounts\" AS") && !command.CommandText.Contains("JOIN"))
            {
                invoked = true;
                await action();
            }
            return result;
        }
    }

    [Fact]
    public async Task Full_balance_retry_committed_between_reference_and_balance_reads_replays()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var winner = new CivicPayDbContext(Options(app));
        var balance = (await winner.Accounts.SingleAsync(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001")).Balance;
        PaymentResponse? original = null;
        var interceptor = new BeforeAccountQuery(async () => original = await new PaymentService(winner, TimeProvider.System).AcceptAsync(Payment(balance), null, default));
        await using var retry = new CivicPayDbContext(Options(app, interceptor));
        var replay = await new PaymentService(retry, TimeProvider.System).AcceptAsync(Payment(balance), null, default);
        Assert.NotNull(original);
        Assert.True(replay.Replayed);
        Assert.Equal(original.TransactionId, replay.TransactionId);
        Assert.Equal(0, await winner.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync());
        Assert.Equal(1, await winner.Transactions.CountAsync(x => x.ExternalReference == "AUDIT-RACE"));
    }

    [Fact]
    public async Task Foreign_key_failure_is_not_mislabeled_as_retryable_conflict()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app));
        var before = await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync();
        await Assert.ThrowsAsync<DbUpdateException>(() => new PaymentService(db, TimeProvider.System).AcceptAsync(Payment(), Guid.NewGuid(), default));
        Assert.Equal(before, await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync());
        Assert.Equal(0, await db.Transactions.CountAsync(x => x.ExternalReference == "AUDIT-RACE"));
    }

    [Fact]
    public async Task Account_import_changes_configuration_concurrency_token()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app));
        var before = await new ConfigurationService(db).GetAsync("DEMO-COUNTY", default);
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("municipalityCode,accountNumber,paymentType,balance\nDEMO-COUNTY,AUDIT-ACCOUNT,Utility,50\n"));
        var service = new ImportService(db, new PaymentService(db, TimeProvider.System));
        var batch = await service.ImportAsync(csv, "audit.csv", "accounts", default);
        Assert.Equal("RECONCILED", (await service.ReconcileAsync(batch, default)).Status);
        Assert.NotEqual(before.Version, (await new ConfigurationService(db).GetAsync("DEMO-COUNTY", default)).Version);
    }

    private sealed class FailReportSave : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<ImportRecord>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Injected reporting failure");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Reporting_failure_rolls_back_payment_and_marks_batch_failed()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app, new FailReportSave()));
        var before = await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync();
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference\nDEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,25,2026-01-01,AUDIT-ROLLBACK\n"));
        var imports = new ImportService(db, new PaymentService(db, TimeProvider.System));
        await Assert.ThrowsAsync<InvalidOperationException>(() => imports.ImportAsync(csv, "audit.csv", "payments", default));
        Assert.Equal(before, await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync());
        Assert.False(await db.Transactions.AnyAsync(x => x.ExternalReference == "AUDIT-ROLLBACK"));
        var batch = await db.ImportBatches.AsNoTracking().SingleAsync();
        Assert.Equal("Failed", batch.Status);
        var report = await imports.ReconcileAsync(batch.Id, default);
        Assert.Equal("FAILED", report.Status);
        Assert.Equal(1, report.SourceRecordCount);
        Assert.False(report.SourceAmountComplete);
    }

    [Theory]
    [InlineData("/api/monitoring/accounts?page=2147483647&pageSize=200")]
    [InlineData("/api/imports/00000000-0000-0000-0000-000000000001/records?page=2147483647&pageSize=200")]
    public async Task Pagination_overflow_is_a_structured_bad_request(string url)
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_PAGINATION", (await response.Content.ReadFromJsonAsync<ApiError>())!.ErrorCode);
    }

    [Theory]
    [InlineData("/api/not-an-endpoint", "GET", 404, "ENDPOINT_NOT_FOUND")]
    [InlineData("/api/payments", "DELETE", 405, "METHOD_NOT_ALLOWED")]
    [InlineData("/api/payments", "POST", 415, "UNSUPPORTED_MEDIA_TYPE")]
    public async Task Framework_errors_use_the_documented_error_envelope(string url, string method, int status, string code)
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), url);
        if (method == "POST")
            request.Content = new StringContent("{}", Encoding.UTF8, "text/plain");
        var response = await client.SendAsync(request);
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<ApiError>())!.ErrorCode);
    }

    [Fact]
    public async Task Invalid_utf8_import_is_rejected_before_creating_a_batch()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("accounts"), "kind");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("municipalityCode,accountNumber,paymentType,balance\nDEMO-COUNTY,").Concat(new byte[] { 0xff }).Concat(Encoding.UTF8.GetBytes(",Utility,50\n")).ToArray()), "file", "audit.csv");
        var response = await client.PostAsync("/api/imports", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("INVALID_CSV_ENCODING", (await response.Content.ReadFromJsonAsync<ApiError>())!.ErrorCode);
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<CivicPayDbContext>().ImportBatches.AnyAsync());
    }

    [Fact]
    public async Task Monitoring_filters_batches_before_applying_display_limit()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app));
        var failed = new ImportBatch { FileName = "older-rejected.csv", Status = "Completed", ExpectedRecordCount = 1, CreatedAt = DateTimeOffset.UtcNow.AddDays(-1), Records = [new ImportRecord { RowNumber = 2, MunicipalityCode = "DEMO-COUNTY", SourceAmount = 25, Status = "Rejected" }] };
        db.ImportBatches.Add(failed);
        for (var i = 0; i < 31; i++)
            db.ImportBatches.Add(new ImportBatch { FileName = "empty-success.csv", Status = "Completed", ExpectedRecordCount = 0 });
        await db.SaveChangesAsync();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/monitoring?status=Rejected");
        Assert.Equal(failed.Id, result.GetProperty("imports")[0].GetProperty("id").GetGuid());
    }
    [Fact]
    public async Task Concurrent_full_balance_http_retries_debit_once()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app));
        var before = await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync();
        var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => client.PostAsJsonAsync("/api/payments", Payment(before))));
        Assert.Equal(1, responses.Count(x => x.StatusCode == HttpStatusCode.Created));
        Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created }));
        var payments = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<PaymentResponse>()));
        Assert.Single(payments.Select(x => x!.TransactionId).Distinct());
        Assert.Equal(0, await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync());
        Assert.Equal(1, await db.Transactions.CountAsync(x => x.ExternalReference == "AUDIT-RACE"));
    }

    [Fact]
    public async Task Concurrent_same_payment_imports_report_one_import_and_one_skip()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        async Task<ReconciliationResult> Upload()
        {
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent("payments"), "kind");
            form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference\nDEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,25,2026-01-01,AUDIT-CONCURRENT-IMPORT\n")), "file", "audit.csv");
            var response = await client.PostAsync("/api/imports", form);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<ReconciliationResult>())!;
        }
        var reports = await Task.WhenAll(Upload(), Upload());
        Assert.Equal(1, reports.Sum(x => x.ImportedRecordCount));
        Assert.Equal(1, reports.Sum(x => x.SkippedRecordCount));
        Assert.Equal(25, reports.Sum(x => x.TotalImportedAmount));
        await using var db = new CivicPayDbContext(Options(app));
        Assert.Equal(1, await db.Transactions.CountAsync(x => x.ExternalReference == "AUDIT-CONCURRENT-IMPORT"));
    }

    [Fact]
    public async Task Service_upload_size_limit_counts_bytes_including_multibyte_utf8()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app));
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes(new string('é', 1_000_001)));
        var exception = await Assert.ThrowsAsync<BusinessException>(() => new ImportService(db, new PaymentService(db, TimeProvider.System)).ImportAsync(csv, "audit.csv", "accounts", default));
        Assert.Equal("IMPORT_TOO_LARGE", exception.Code);
        Assert.Equal(413, exception.Status);
        Assert.False(await db.ImportBatches.AnyAsync());
    }

    [Fact]
    public async Task Large_multipart_request_returns_413_envelope()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("accounts"), "kind");
        form.Add(new ByteArrayContent(new byte[2_100_001]), "file", "audit.csv");
        var response = await client.PostAsync("/api/imports", form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("IMPORT_TOO_LARGE", (await response.Content.ReadFromJsonAsync<ApiError>())!.ErrorCode);
    }

    [Fact]
    public async Task Configuration_code_cannot_end_with_a_newline()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/municipalities", new ConfigurationRequest("AUDIT\n", "Audit", "CAD", true, 5, ["Utility"]));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("INVALID_CONFIGURATION", (await response.Content.ReadFromJsonAsync<ApiError>())!.ErrorCode);
    }

    [Fact]
    public async Task Monitoring_utc_date_and_municipality_filters_exclude_other_records()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app));
        db.IntegrationEvents.Add(new IntegrationEvent { CorrelationId = "AUDIT-DATE", Path = "/api/payments", Method = "POST", MunicipalityCode = "DEMO-COUNTY", StatusCode = 201, CreatedAt = new DateTimeOffset(2001, 2, 3, 23, 0, 0, TimeSpan.FromHours(-2)) });
        db.IntegrationEvents.Add(new IntegrationEvent { CorrelationId = "AUDIT-OTHER", Path = "/api/payments", Method = "POST", MunicipalityCode = "RIVERBEND", StatusCode = 201, CreatedAt = new DateTimeOffset(2001, 2, 4, 1, 0, 0, TimeSpan.Zero) });
        await db.SaveChangesAsync();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/monitoring?municipality=DEMO-COUNTY&from=2001-02-04&to=2001-02-04");
        Assert.Equal(1, result.GetProperty("metrics").GetProperty("totalApiRequests").GetInt32());
        Assert.Equal("2001-02-04", result.GetProperty("daily")[0].GetProperty("date").GetString());
        Assert.Empty(result.GetProperty("transactions").EnumerateArray());
    }

    private sealed class FailPaymentSave : SaveChangesInterceptor
    {
        public int Attempts
        {
            get; private set;
        }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<PaymentTransaction>().Any(x => x.State == EntityState.Added && x.Entity.ExternalReference == "AUDIT-RACE"))
            {
                Attempts++;
                throw new DbUpdateException("Synthetic confidential database diagnostic");
            }
            return ValueTask.FromResult(result);
        }
    }

    private sealed class FailingPaymentApp : TestApp
    {
        public FailPaymentSave Failure { get; } = new();
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureServices(services => services.AddDbContext<CivicPayDbContext>(options => options.AddInterceptors(Failure)));
        }
    }

    [Fact]
    public async Task Unexpected_database_failure_returns_safe_500_and_persists_correlation()
    {
        using var app = new FailingPaymentApp();
        using var client = app.CreateClient();
        // The fault interceptor targets this audit reference; synthetic seed inserts are unaffected.
        var response = await client.PostAsJsonAsync("/api/payments", Payment());
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        var text = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("confidential", text);
        var error = JsonSerializer.Deserialize<ApiError>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("INTERNAL_ERROR", error.ErrorCode);
        Assert.Equal(1, app.Failure.Attempts);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
        Assert.True(await db.ErrorLogs.AnyAsync(x => x.CorrelationId == error.CorrelationId && x.ErrorCode == "INTERNAL_ERROR"));
        Assert.False(await db.Transactions.AnyAsync(x => x.ExternalReference == "AUDIT-RACE"));
    }

    private sealed class FailReportAndCleanup : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (data.Context!.ChangeTracker.Entries<ImportRecord>().Any(x => x.State == EntityState.Added))
                throw new InvalidOperationException("Original reporting failure");
            if (data.Context.ChangeTracker.Entries<ImportBatch>().Any(x => x.Entity.Status == "Failed"))
                throw new InvalidOperationException("Subsequent cleanup failure");
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task Import_cleanup_failure_preserves_original_exception_and_rolls_back()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app, new FailReportAndCleanup()));
        var before = await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync();
        using var csv = new MemoryStream(Encoding.UTF8.GetBytes("municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference\nDEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,25,2026-01-01,AUDIT-CLEANUP\n"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => new ImportService(db, new PaymentService(db, TimeProvider.System), NullLogger<ImportService>.Instance).ImportAsync(csv, "audit.csv", "payments", default));
        Assert.Equal("Original reporting failure", error.Message);
        Assert.False(await db.Transactions.AnyAsync(x => x.ExternalReference == "AUDIT-CLEANUP"));
        Assert.Equal(before, await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync());
    }

    [Fact]
    public async Task Synthetic_seed_events_cannot_fabricate_measured_request_metrics()
    {
        using var app = new TestApp();
        using var client = app.CreateClient();
        await using var db = new CivicPayDbContext(Options(app));
        db.IntegrationEvents.Add(new IntegrationEvent { CorrelationId = "synthetic-legacy-event", Path = "/api/payments", Method = "POST", StatusCode = 201, DurationMs = 12345 });
        await db.SaveChangesAsync();
        var result = await client.GetFromJsonAsync<JsonElement>("/api/monitoring");
        Assert.Equal(0, result.GetProperty("metrics").GetProperty("totalApiRequests").GetInt32());
        Assert.Equal(0, result.GetProperty("metrics").GetProperty("averageResponseMs").GetDouble());
        Assert.Equal(168, result.GetProperty("metrics").GetProperty("successfulTransactions").GetInt32());
        Assert.Empty(result.GetProperty("daily").EnumerateArray());
    }

    private sealed class MarsApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("Database:Provider", "SqlServer");
            builder.UseSetting("Database:Initialize", "false");
            builder.UseSetting("Database:Seed", "false");
            builder.UseSetting("ConnectionStrings:CivicPay", "Server=localhost;Database=CivicPayTests_Guard;Integrated Security=True;MultipleActiveResultSets=True");
        }
    }

    [Fact]
    public void Startup_rejects_MARS_before_connecting_to_a_database()
    {
        using var app = new MarsApp();
        var error = Assert.Throws<InvalidOperationException>(() => app.CreateClient());
        Assert.Contains("Disable MultipleActiveResultSets", error.Message);
    }

    private sealed class ForeignKeysOffApp : TestApp
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("ConnectionStrings:CivicPay", $"Data Source={DatabasePath};Foreign Keys=False;Default Timeout=30");
        }
    }

    [Fact]
    public async Task Custom_SQLite_connection_cannot_disable_relational_integrity()
    {
        using var app = new ForeignKeysOffApp();
        using var client = app.CreateClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
        var account = await db.Accounts.FirstAsync();
        db.Transactions.Add(new PaymentTransaction { MunicipalityId = account.MunicipalityId, AccountId = account.Id, PaymentTypeId = account.PaymentTypeId, Amount = 25, TransactionDate = DateOnly.FromDateTime(DateTime.UtcNow), ExternalReference = "AUDIT-FOREIGN-KEY", ImportBatchId = Guid.NewGuid() });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.False(await db.Transactions.AnyAsync(x => x.ExternalReference == "AUDIT-FOREIGN-KEY"));
    }

}
