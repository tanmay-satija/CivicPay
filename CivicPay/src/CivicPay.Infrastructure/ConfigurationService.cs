using CivicPay.Application;
using CivicPay.Domain;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.Infrastructure;

public class ConfigurationService(CivicPayDbContext db) : IConfigurationService
{
    private IQueryable<Municipality> Query => db.Municipalities.Include(x => x.Configuration).Include(x => x.PaymentTypes).ThenInclude(x => x.PaymentType);
    public static ConfigurationResponse Map(Municipality m) => new(m.Id, m.Code, m.Name, m.Configuration.Currency, m.Configuration.AllowPartialPayments, m.Configuration.MinimumPayment, m.PaymentTypes.Select(x => x.PaymentType.Code).Order().ToArray(), m.Configuration.Version);
    public async Task<List<ConfigurationResponse>> ListAsync(CancellationToken ct) => (await Query.AsNoTracking().OrderBy(x => x.Code).ToListAsync(ct)).Select(Map).ToList();
    public async Task<ConfigurationResponse> GetAsync(string code, CancellationToken ct) => Map(await Query.AsNoTracking().SingleOrDefaultAsync(x => x.Code == Rules.Code(code), ct) ?? throw new BusinessException("MUNICIPALITY_NOT_FOUND", "Municipality was not found.", 404));
    public async Task<ConfigurationResponse> SaveAsync(ConfigurationRequest r, bool create, CancellationToken ct)
    {
        Rules.Configuration(r);
        var code = Rules.Code(r.MunicipalityCode);
        var m = await Query.SingleOrDefaultAsync(x => x.Code == code, ct);
        Rules.Require(!create || m == null, "MUNICIPALITY_EXISTS", "Municipality code is already configured.", 409);
        Rules.Require(create || m != null, "MUNICIPALITY_NOT_FOUND", "Municipality was not found.", 404);
        if (m != null)
            Rules.Require(r.Version == m.Configuration.Version, "CONFIGURATION_CONFLICT", "Supply the current version when updating configuration.", 409);
        var types = await db.PaymentTypes.Where(x => r.AcceptedPaymentTypes.Contains(x.Code)).ToListAsync(ct);
        if (types.Count != r.AcceptedPaymentTypes.Length)
            throw new InvalidOperationException("Required payment catalogue is missing. Initialize the database before accepting configuration.");
        if (m == null)
        {
            m = new Municipality { Code = code };
            db.Municipalities.Add(m);
        }
        else
        {
            var removed = m.PaymentTypes.Select(x => x.PaymentTypeId).Except(types.Select(x => x.Id)).ToArray();
            Rules.Require(!await db.Accounts.AnyAsync(x => x.MunicipalityId == m.Id && removed.Contains(x.PaymentTypeId) && x.Balance > 0, ct), "ACTIVE_ACCOUNTS", "A payment type with outstanding accounts cannot be disabled.", 409);
            db.MunicipalityPaymentTypes.RemoveRange(m.PaymentTypes.Where(x => removed.Contains(x.PaymentTypeId)));
        }
        m.Name = r.MunicipalityName.Trim();
        m.Configuration.Currency = r.Currency;
        m.Configuration.MinimumPayment = r.MinimumPayment;
        m.Configuration.AllowPartialPayments = r.AllowPartialPayments;
        m.Configuration.Version = Guid.NewGuid();
        m.Configuration.UpdatedAt = DateTimeOffset.UtcNow;
        foreach (var type in types.Where(t => !m.PaymentTypes.Any(x => x.PaymentTypeId == t.Id)))
            m.PaymentTypes.Add(new()
            {
                PaymentTypeId = type.Id,
                PaymentType = type
            });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException) { throw new BusinessException("CONFIGURATION_CONFLICT", "Configuration changed concurrently. Reload and retry.", 409); }
        catch (DbUpdateException ex) when (DatabaseErrors.IsUniqueViolation(ex)) { throw new BusinessException("DATABASE_CONFLICT", "Configuration could not be saved. Reload and retry.", 409); }
        db.ChangeTracker.Clear();
        return await GetAsync(code, ct);
    }
}
