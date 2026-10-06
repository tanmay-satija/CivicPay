using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CivicPay.Application;
using CivicPay.Domain;
using CivicPay.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace CivicPay.IntegrationTests;

public class ApiTests : IDisposable
{
    private readonly TestApp app = new(); private readonly HttpClient client;
    public ApiTests() => client = app.CreateClient();
    public void Dispose()
    {
        client.Dispose();
        app.Dispose();
    }
    private static PaymentRequest Payment(string reference = "TEST-PAYMENT") => new("DEMO-COUNTY", "PROPERTYTAX-0001", "PropertyTax", 25, DateOnly.FromDateTime(DateTime.UtcNow), reference);
    private async Task<ApiError> Error(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        Assert.Equal(status, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.NotNull(error);
        Assert.False(error.Success);
        Assert.Equal(code, error.ErrorCode);
        Assert.NotEmpty(error.CorrelationId!);
        Assert.True(response.Headers.Contains("X-Correlation-ID"));
        return error;
    }
    [Fact]
    public async Task Payment_success_replay_and_conflict_are_atomic()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
        var before = await db.Accounts.AsNoTracking().SingleAsync(x => x.AccountNumber == "PROPERTYTAX-0001" && x.Municipality.Code == "DEMO-COUNTY");
        var balance = before.Balance;
        var first = await client.PostAsJsonAsync("/api/payments", Payment());
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var p = await first.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.NotNull(p);
        Assert.False(p.Replayed);
        Assert.NotNull(first.Headers.Location);
        var second = await client.PostAsJsonAsync("/api/payments", Payment());
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var replay = await second.Content.ReadFromJsonAsync<PaymentResponse>();
        Assert.True(replay!.Replayed);
        Assert.Equal(p.TransactionId, replay.TransactionId);
        await Error(await client.PostAsJsonAsync("/api/payments", Payment() with
        {
            Amount = 26
        }), HttpStatusCode.Conflict, "DUPLICATE_REFERENCE");
        Assert.Equal(balance - 25, (await db.Accounts.AsNoTracking().SingleAsync(x => x.Id == before.Id)).Balance);
        Assert.Equal(1, await db.Transactions.CountAsync(x => x.ExternalReference == "TEST-PAYMENT"));
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(first.Headers.Location)).StatusCode);
    }
    [Theory]
    [InlineData("Missing", "PropertyTax", 25, "ACCOUNT_NOT_FOUND", 404)]
    [InlineData("PROPERTYTAX-0001", "ParkingTicket", 25, "UNSUPPORTED_PAYMENT_TYPE", 422)]
    [InlineData("PROPERTYTAX-0001", "PropertyTax", 1, "BELOW_MINIMUM", 422)]
    [InlineData("PROPERTYTAX-0001", "PropertyTax", -10, "INVALID_AMOUNT", 422)]
    [InlineData("PROPERTYTAX-0001", "Utility", 25, "ACCOUNT_TYPE_MISMATCH", 422)]
    public async Task Payment_validation_returns_structured_errors(string account, string type, decimal amount, string code, int status) => await Error(await client.PostAsJsonAsync("/api/payments", Payment() with { AccountNumber = account, PaymentType = type, Amount = amount }), (HttpStatusCode)status, code);
    [Fact] public async Task Unknown_municipality_is_rejected() => await Error(await client.PostAsJsonAsync("/api/payments", Payment() with { MunicipalityCode = "UNKNOWN" }), HttpStatusCode.NotFound, "MUNICIPALITY_NOT_FOUND");
    [Fact]
    public async Task Full_payment_client_rejects_partial_and_accepts_full()
    {
        var p = Payment() with
        {
            MunicipalityCode = "PINE-VALLEY",
            AccountNumber = "PARKINGTICKET-0030",
            PaymentType = "ParkingTicket"
        };
        await Error(await client.PostAsJsonAsync("/api/payments", p), HttpStatusCode.UnprocessableEntity, "PARTIAL_PAYMENT_NOT_ALLOWED");
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/payments", p with
        {
            Amount = 5000
        })).StatusCode);
    }
    [Fact]
    public async Task Invalid_json_is_safe_and_logged()
    {
        var response = await client.PostAsync("/api/payments", new StringContent("{bad-json", Encoding.UTF8, "application/json"));
        await Error(response, HttpStatusCode.BadRequest, "INVALID_REQUEST");
        using var scope = app.Services.CreateScope();
        Assert.True(await scope.ServiceProvider.GetRequiredService<CivicPayDbContext>().ErrorLogs.AnyAsync(x => x.ErrorCode == "INVALID_REQUEST"));
    }
    [Fact]
    public async Task Municipality_configuration_create_update_and_version_conflict()
    {
        var request = new ConfigurationRequest("NEW-DEMO", "New Demo", "CAD", true, 15, ["Utility"]);
        var created = await client.PostAsJsonAsync("/api/municipalities", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var m = await created.Content.ReadFromJsonAsync<ConfigurationResponse>();
        var update = request with
        {
            MinimumPayment = 20,
            Version = m!.Version
        };
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/api/municipalities/NEW-DEMO", update)).StatusCode);
        await Error(await client.PutAsJsonAsync("/api/municipalities/NEW-DEMO", update), HttpStatusCode.Conflict, "CONFIGURATION_CONFLICT");
        var types = await client.GetFromJsonAsync<string[]>("/api/municipalities/NEW-DEMO/payment-types");
        Assert.NotNull(types);
        Assert.Equal(["Utility"], types);
        await Error(await client.PostAsJsonAsync("/api/municipalities", request with
        {
            MinimumPayment = -1
        }), HttpStatusCode.UnprocessableEntity, "INVALID_CONFIGURATION");
    }
    private async Task<ReconciliationResult> Import(string csv, string kind = "payments")
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(kind), "kind");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes(csv)), "file", "synthetic.csv");
        var result = await client.PostAsync("/api/imports", form);
        Assert.Equal(HttpStatusCode.Created, result.StatusCode);
        return (await result.Content.ReadFromJsonAsync<ReconciliationResult>())!;
    }
    [Fact]
    public async Task Disabled_type_with_outstanding_accounts_cannot_be_removed()
    {
        var m = await client.GetFromJsonAsync<ConfigurationResponse>("/api/municipalities/DEMO-COUNTY");
        var r = new ConfigurationRequest(m!.MunicipalityCode, m.MunicipalityName, m.Currency, m.AllowPartialPayments, m.MinimumPayment, ["Utility"], m.Version);
        await Error(await client.PutAsJsonAsync("/api/municipalities/DEMO-COUNTY", r), HttpStatusCode.Conflict, "ACTIVE_ACCOUNTS");
    }
    [Fact]
    public async Task Identical_reference_is_independent_between_municipalities()
    {
        var first = await client.PostAsJsonAsync("/api/payments", Payment("SHARED-REF"));
        var other = await client.PostAsJsonAsync("/api/payments", Payment("SHARED-REF") with
        {
            MunicipalityCode = "RIVERBEND",
            AccountNumber = "UTILITY-0001",
            PaymentType = "Utility"
        });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
        Assert.NotEqual((await first.Content.ReadFromJsonAsync<PaymentResponse>())!.TransactionId, (await other.Content.ReadFromJsonAsync<PaymentResponse>())!.TransactionId);
    }
    private const string Header = "municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference\n";
    [Fact]
    public async Task Valid_import_reconciles_and_repeat_skips()
    {
        var csv = Header + "DEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,25,2026-01-01,CSV-1\nDEMO-COUNTY,UTILITY-0002,Utility,30,2026-01-01,CSV-2\n";
        var r = await Import(csv);
        Assert.Equal("RECONCILED", r.Status);
        Assert.Equal(2, r.ImportedRecordCount);
        Assert.Equal(55, r.TotalImportedAmount);
        Assert.Equal(0, r.Difference);
        var replay = await Import(csv);
        Assert.Equal(2, replay.SkippedRecordCount);
        Assert.Equal(0, replay.TotalImportedAmount);
        Assert.Equal("WARNING", replay.Status);
        var retrieved = await client.GetFromJsonAsync<ReconciliationResult>($"/api/imports/{r.BatchId}/reconciliation");
        Assert.Equal(r, retrieved);
    }
    [Fact]
    public async Task Invalid_rows_are_reported_and_totals_are_honest()
    {
        var csv = Header + "DEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,25,2026-01-01,GOOD\nDEMO-COUNTY,,PropertyTax,25,2026-01-01,BAD-1\nDEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,-10,2026-01-01,BAD-2\nDEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,nope,2026-01-01,BAD-3\nDEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,25,nonsense,BAD-4\n";
        var r = await Import(csv);
        Assert.Equal(5, r.SourceRecordCount);
        Assert.Equal(1, r.ImportedRecordCount);
        Assert.Equal(4, r.RejectedRecordCount);
        Assert.False(r.SourceAmountComplete);
        Assert.Equal(65, r.TotalSourceAmount);
        Assert.Equal(40, r.Difference);
        Assert.Equal("WARNING", r.Status);
        var rows = await client.GetFromJsonAsync<JsonElement>($"/api/imports/{r.BatchId}/records");
        Assert.Equal(5, rows.GetProperty("total").GetInt32());
        Assert.Equal("REQUIRED_FIELDS", rows.GetProperty("items")[1].GetProperty("errorCode").GetString());
    }
    [Fact]
    public async Task All_rejected_import_is_failed()
    {
        var r = await Import(Header + "UNKNOWN,A,Utility,10,2026-01-01,BAD\n");
        Assert.Equal("FAILED", r.Status);
        Assert.Equal(1, r.RejectedRecordCount);
    }
    [Fact]
    public async Task Accounts_import_reconciles_and_enables_payment()
    {
        var r = await Import("municipalityCode,accountNumber,paymentType,balance\nDEMO-COUNTY,LEGACY-01,Utility,100\n", "accounts");
        Assert.Equal("RECONCILED", r.Status);
        Assert.Equal(100, r.TotalImportedAmount);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/payments", Payment() with
        {
            AccountNumber = "LEGACY-01",
            PaymentType = "Utility"
        })).StatusCode);
    }
    [Fact]
    public async Task Bad_csv_header_fails_before_creating_batch()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("payments"), "kind");
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("incorrect,header\na,b")), "file", "bad.csv");
        await Error(await client.PostAsync("/api/imports", form), HttpStatusCode.BadRequest, "INVALID_CSV_HEADER");
        using var scope = app.Services.CreateScope();
        Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<CivicPayDbContext>().ImportBatches.CountAsync());
    }
    [Fact]
    public async Task Health_dashboard_and_swagger_are_served()
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health")).StatusCode);
        Assert.Contains("CivicPay", await client.GetStringAsync("/"));
        Assert.Contains("PaymentRequest", await client.GetStringAsync("/swagger/v1/swagger.json"));
        var snapshot = await client.GetFromJsonAsync<JsonElement>("/api/monitoring?municipality=DEMO-COUNTY");
        Assert.True(snapshot.GetProperty("metrics").GetProperty("successfulTransactions").GetInt32() > 0);
        Assert.All(snapshot.GetProperty("transactions").EnumerateArray(), x => Assert.Equal("DEMO-COUNTY", x.GetProperty("municipalityCode").GetString()));
        await Error(await client.GetAsync("/api/monitoring?from=2026-02-01&to=2026-01-01"), HttpStatusCode.BadRequest, "INVALID_DATE_RANGE");
    }
    [Fact]
    public async Task Database_constraints_reject_negative_balance()
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
        var a = await db.Accounts.FirstAsync();
        a.Balance = -1;
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
    [Fact]
    public async Task Parallel_retry_never_creates_two_records()
    {
        using var beforeScope = app.Services.CreateScope();
        var beforeDb = beforeScope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
        var balance = await beforeDb.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync();
        var requests = Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/api/payments", Payment("RACE-REF")));
        var responses = await Task.WhenAll(requests);
        Assert.Contains(responses, x => x.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, x => Assert.Contains(x.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Created, HttpStatusCode.Conflict }));
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CivicPayDbContext>();
        Assert.Equal(1, await db.Transactions.CountAsync(x => x.ExternalReference == "RACE-REF"));
        Assert.Equal(balance - 25, await db.Accounts.AsNoTracking().Where(x => x.Municipality.Code == "DEMO-COUNTY" && x.AccountNumber == "PROPERTYTAX-0001").Select(x => x.Balance).SingleAsync());
    }
}
