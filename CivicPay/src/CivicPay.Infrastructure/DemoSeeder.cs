using CivicPay.Application;
using CivicPay.Domain;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.Infrastructure;

public static class DemoSeeder
{
    public static async Task SeedAsync(CivicPayDbContext db, CancellationToken ct = default)
    {
        await PaymentCatalogue.EnsureAsync(db, ct);
        if (await db.Municipalities.AnyAsync(ct))
            return;
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var types = await db.PaymentTypes.ToListAsync(ct);
        (string Code, string Name, bool Partial, decimal Minimum, string[] Types)[] clients = [("DEMO-COUNTY", "Demo County", true, 5, ["PropertyTax", "Utility", "Permit"]), ("PINE-VALLEY", "Pine Valley", false, 10, ["PropertyTax", "ParkingTicket"]), ("RIVERBEND", "Riverbend", true, 25, ["Utility", "BusinessLicence", "Permit"])];
        foreach (var c in clients)
        {
            var m = new Municipality { Code = c.Code, Name = c.Name, Configuration = new() { AllowPartialPayments = c.Partial, MinimumPayment = c.Minimum }, PaymentTypes = types.Where(x => c.Types.Contains(x.Code)).Select(x => new MunicipalityPaymentType { PaymentTypeId = x.Id }).ToList() };
            db.Municipalities.Add(m);
            await db.SaveChangesAsync(ct);
            for (int i = 1; i <= 30; i++)
            {
                var type = types.Single(x => x.Code == c.Types[(i - 1) % c.Types.Length]);
                db.Accounts.Add(new()
                {
                    MunicipalityId = m.Id,
                    AccountNumber = $"{type.Code.ToUpperInvariant()}-{i:0000}",
                    PaymentTypeId = type.Id,
                    Balance = 5000
                });
            }
            await db.SaveChangesAsync(ct);
        }
        var accounts = await db.Accounts.Include(x => x.Municipality).ToListAsync(ct);
        var now = DateTimeOffset.UtcNow;
        foreach (var a in accounts.Where(a => a.Id % 5 != 0))
        {
            int count = a.Municipality.Configuration.AllowPartialPayments ? 3 : 1;
            for (int j = 0; j < count; j++)
            {
                decimal amount = count == 1 ? 5000 : 100 + (a.Id * 37 + j * 29) % 600;
                a.Balance -= amount;
                var at = now.AddDays(-((a.Id + j) % 28)).AddMinutes(-a.Id);
                db.Transactions.Add(new()
                {
                    MunicipalityId = a.MunicipalityId,
                    AccountId = a.Id,
                    PaymentTypeId = a.PaymentTypeId,
                    Amount = amount,
                    ExternalReference = $"SEED-{a.Id}-{j}",
                    TransactionDate = DateOnly.FromDateTime(at.UtcDateTime),
                    CreatedAt = at
                });

            }
        }
        for (int i = 0; i < 12; i++)
        {
            var c = clients[i % 3];
            var code = i % 2 == 0 ? "UNSUPPORTED_PAYMENT_TYPE" : "ACCOUNT_NOT_FOUND";
            var correlation = $"synthetic-error-{i}";
            db.ErrorLogs.Add(new()
            {
                CorrelationId = correlation,
                MunicipalityCode = c.Code,
                ErrorCode = code,
                Message = "Synthetic onboarding validation scenario.",
                CreatedAt = now.AddHours(-i * 4)
            });

        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        db.ChangeTracker.Clear();
    }
}
