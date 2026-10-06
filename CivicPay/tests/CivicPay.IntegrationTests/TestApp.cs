using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
namespace CivicPay.IntegrationTests;

public class TestApp : WebApplicationFactory<Program>
{
    public string DatabasePath { get; } = Path.Combine(Path.GetTempPath(), $"civicpay-test-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder b)
    {
        b.UseEnvironment("Testing");
        b.UseSetting("Database:Provider", "Sqlite");
        b.UseSetting("ConnectionStrings:CivicPay", $"Data Source={DatabasePath};Foreign Keys=True;Default Timeout=30");
        b.UseSetting("Database:Initialize", "true");
        b.UseSetting("Database:Seed", "true");
        b.UseSetting("Demo:OpenAccess", "true");
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={DatabasePath};Foreign Keys=True;Default Timeout=30");
        Microsoft.Data.Sqlite.SqliteConnection.ClearPool(connection);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
            if (File.Exists(DatabasePath + suffix))
                File.Delete(DatabasePath + suffix);
    }
}
