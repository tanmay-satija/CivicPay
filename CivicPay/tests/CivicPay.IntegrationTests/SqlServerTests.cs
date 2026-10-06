using CivicPay.Application;
using System.Text;
using CivicPay.Infrastructure;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.IntegrationTests;

public sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CIVICPAY_TEST_SQLSERVER")))
            Skip = "Set CIVICPAY_TEST_SQLSERVER to a disposable CivicPayTests_* SQL Server database connection string.";
    }
}
public class SqlServerTests
{
    [SqlServerFact]
    public async Task SqlServer_migrations_constraints_and_seeding()
    {
        var connection = Environment.GetEnvironmentVariable("CIVICPAY_TEST_SQLSERVER")!;
        var parsed = new SqlConnectionStringBuilder(connection);
        Assert.StartsWith("CivicPayTests_", parsed.InitialCatalog);
        // Never migrate or delete the caller's named database: own a unique database for this run.
        parsed.InitialCatalog = "CivicPayTests_" + Guid.NewGuid().ToString("N");
        parsed.MultipleActiveResultSets = false;
        var options = new DbContextOptionsBuilder<CivicPayDbContext>().UseSqlServer(parsed.ConnectionString).Options;
        await using var db = new CivicPayDbContext(options);
        try
        {
            await db.Database.MigrateAsync();
            Assert.True(await db.Database.CanConnectAsync());
            Assert.Equal(5, await db.PaymentTypes.CountAsync());
            var configured = await new ConfigurationService(db).SaveAsync(new ConfigurationRequest("UNSEEDED", "Unseeded", "CAD", true, 5, ["Utility"]), true, default);
            Assert.Equal(["Utility"], configured.AcceptedPaymentTypes);
            // Remove the catalogue-only setup so the synthetic seed remains isolated.
            db.ChangeTracker.Clear();
            await db.MunicipalityPaymentTypes.Where(x => x.MunicipalityId == configured.Id).ExecuteDeleteAsync();
            await db.Configurations.Where(x => x.MunicipalityId == configured.Id).ExecuteDeleteAsync();
            await db.Municipalities.Where(x => x.Id == configured.Id).ExecuteDeleteAsync();
            await DemoSeeder.SeedAsync(db);
            Assert.Equal(3, await db.Municipalities.CountAsync());
            var service = new PaymentService(db, TimeProvider.System);
            var request = new PaymentRequest("DEMO-COUNTY", "PROPERTYTAX-0001", "PropertyTax", 25, DateOnly.FromDateTime(DateTime.UtcNow), "SQL-RETRY");
            var first = await service.AcceptAsync(request, null, default);
            var replay = await service.AcceptAsync(request, null, default);
            Assert.Equal(first.TransactionId, replay.TransactionId);
            Assert.True(replay.Replayed);
            var lowerCase = await service.AcceptAsync(request with
            {
                ExternalReference = "sql-retry"
            }, null, default);
            Assert.False(lowerCase.Replayed);
            Assert.NotEqual(first.TransactionId, lowerCase.TransactionId);
            Assert.Equal("DUPLICATE_REFERENCE", (await Assert.ThrowsAsync<BusinessException>(() => service.AcceptAsync(request with { Amount = 26 }, null, default))).Code);
            var imports = new ImportService(db, service);
            using var csv = new MemoryStream(Encoding.UTF8.GetBytes("municipalityCode,accountNumber,paymentType,amount,transactionDate,externalReference\nDEMO-COUNTY,PROPERTYTAX-0001,PropertyTax,30,2026-01-01,SQL-CSV\n"));
            var batch = await imports.ImportAsync(csv, "sql-test.csv", "payments", default);
            Assert.Equal("RECONCILED", (await imports.ReconcileAsync(batch, default)).Status);
            db.ChangeTracker.Clear();
            var account = await db.Accounts.FirstAsync();
            account.Balance = -1;
            await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        }
        finally { await db.Database.EnsureDeletedAsync(); }
    }
}
