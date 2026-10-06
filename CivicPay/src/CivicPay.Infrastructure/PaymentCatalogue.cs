using CivicPay.Application;
using CivicPay.Domain;
using Microsoft.EntityFrameworkCore;

namespace CivicPay.Infrastructure;

public static class PaymentCatalogue
{
    // Reference data is required even when synthetic municipalities/accounts are disabled.
    public static async Task EnsureAsync(CivicPayDbContext db, CancellationToken ct = default)
    {
        var existing = await db.PaymentTypes.Select(x => x.Code).ToListAsync(ct);
        db.PaymentTypes.AddRange(Rules.PaymentTypes.Except(existing).Select(code => new PaymentType { Code = code }));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex))
        {
            // Another startup may have installed the same catalogue concurrently.
            db.ChangeTracker.Clear();
            var installed = await db.PaymentTypes.Select(x => x.Code).ToListAsync(ct);
            if (Rules.PaymentTypes.Except(installed).Any())
                throw;
        }
    }
}
