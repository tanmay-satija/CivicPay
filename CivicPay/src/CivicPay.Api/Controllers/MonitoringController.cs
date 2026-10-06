using CivicPay.Application;
using CivicPay.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.Api.Controllers;

[ApiController, Route("api/monitoring")]
public class MonitoringController(CivicPayDbContext db, IConfigurationService configuration) : ControllerBase
{
    /// <summary>Dashboard snapshot filtered by municipality, inclusive UTC dates and Accepted/Rejected request outcome.</summary>
    [HttpGet]
    public async Task<IActionResult> Snapshot(string? municipality, DateOnly? from, DateOnly? to, string? status, CancellationToken ct)
    {
        Rules.Require(!from.HasValue || !to.HasValue || from <= to, "INVALID_DATE_RANGE", "From date must not exceed to date.", 400);
        Rules.Require(status == null || status is "Accepted" or "Rejected", "INVALID_STATUS", "Status must be Accepted or Rejected.", 400);
        var code = string.IsNullOrWhiteSpace(municipality) ? null : Rules.Code(municipality);
        bool Match(string? m, DateTimeOffset date) => (code == null || m == code) && (!from.HasValue || DateOnly.FromDateTime(date.UtcDateTime) >= from) && (!to.HasValue || DateOnly.FromDateTime(date.UtcDateTime) <= to);
        // Filter in the database before materialization. Metrics include all matches; display lists have limits.
        var eventQuery = DateFiltered(db.IntegrationEvents, from, to).AsNoTracking().Where(x => !x.CorrelationId.StartsWith("synthetic-"));
        var errorQuery = DateFiltered(db.ErrorLogs, from, to).AsNoTracking();
        var transactionQuery = DateFiltered(db.Transactions, from, to).AsNoTracking();
        var batchQuery = DateFiltered(db.ImportBatches, from, to).AsNoTracking();
        if (code != null)
        {
            eventQuery = eventQuery.Where(x => x.MunicipalityCode == code);
            errorQuery = errorQuery.Where(x => x.MunicipalityCode == code);
            transactionQuery = transactionQuery.Where(x => x.Municipality.Code == code);
            batchQuery = batchQuery.Where(x => x.Records.Any(r => r.MunicipalityCode == code));
        }
        if (status != null)
            eventQuery = eventQuery.Where(x => status == "Accepted" ? x.StatusCode < 400 : x.StatusCode >= 400);
        if (status == "Accepted")
            errorQuery = errorQuery.Where(x => false);
        if (status == "Rejected")
            transactionQuery = transactionQuery.Where(x => false);
        var events = (await eventQuery.ToListAsync(ct)).Where(x => Match(x.MunicipalityCode, x.CreatedAt) && (status == null || (status == "Accepted" ? x.StatusCode < 400 : x.StatusCode >= 400))).ToList();
        var errors = (await errorQuery.ToListAsync(ct)).Where(x => Match(x.MunicipalityCode, x.CreatedAt) && status != "Accepted").OrderByDescending(x => x.CreatedAt).Take(100).Select(x => new { x.Id, x.CreatedAt, x.MunicipalityCode, x.ErrorCode, x.Message, x.CorrelationId }).ToList();
        var transactions = (await transactionQuery.Select(x => new { x.Id, municipalityCode = x.Municipality.Code, municipalityName = x.Municipality.Name, accountNumber = x.Account.AccountNumber, paymentType = x.PaymentType.Code, x.Amount, x.TransactionDate, x.ExternalReference, x.Status, x.CreatedAt }).ToListAsync(ct)).Where(x => Match(x.municipalityCode, x.CreatedAt) && status != "Rejected").OrderByDescending(x => x.CreatedAt).ToList();
        var batches = await batchQuery.Include(x => x.Records).AsSplitQuery().ToListAsync(ct);
        var batchResults = new List<object>();
        foreach (var b in batches.Where(x => Match(code, x.CreatedAt)).OrderByDescending(x => x.CreatedAt))
        {
            var report = ImportService.Reconcile(b);
            if (status == "Accepted" && report.Status != "RECONCILED" || status == "Rejected" && report.Status == "RECONCILED")
                continue;
            batchResults.Add(new
            {
                b.Id,
                b.FileName,
                b.Kind,
                b.Status,
                b.CreatedAt,
                reconciliation = report
            });
            if (batchResults.Count == 30)
                break;
        }
        var requests = events.Count;
        var ok = events.Count(x => x.StatusCode < 400);
        var paymentEvents = events.Where(x => x.Path == "/api/payments" && x.Method == "POST").ToList();
        return Ok(new
        {
            generatedAt = DateTimeOffset.UtcNow,
            synthetic = true,
            metrics = new
            {
                totalApiRequests = requests,
                successfulRequests = ok,
                failedRequests = requests - ok,
                successRate = requests == 0 ? 0 : Math.Round(ok * 100.0 / requests, 1),
                successfulTransactions = transactions.Count,
                failedTransactions = paymentEvents.Count(x => x.StatusCode >= 400),
                validationFailures = events.Count(x => x.StatusCode is 400 or 404 or 409 or 422),
                averageResponseMs = requests == 0 ? 0 : Math.Round(events.Average(x => x.DurationMs), 1),
                totalAmount = transactions.Sum(x => x.Amount)
            },
            municipalities = await configuration.ListAsync(ct),
            transactions = transactions.Take(200),
            errors,
            imports = batchResults,
            byMunicipality = transactions.GroupBy(x => new { x.municipalityCode, x.municipalityName }).Select(g => new { code = g.Key.municipalityCode, name = g.Key.municipalityName, count = g.Count(), amount = g.Sum(x => x.Amount) }),
            byPaymentType = transactions.GroupBy(x => x.paymentType).Select(g => new { type = g.Key, count = g.Count(), amount = g.Sum(x => x.Amount) }),
            daily = events.GroupBy(x => DateOnly.FromDateTime(x.CreatedAt.UtcDateTime)).OrderBy(g => g.Key).Select(g => new { date = g.Key, accepted = g.Count(x => x.StatusCode < 400), rejected = g.Count(x => x.StatusCode >= 400) })
        });
    }
    private IQueryable<T> DateFiltered<T>(DbSet<T> entities, DateOnly? from, DateOnly? to) where T : class
    {
        IQueryable<T> query = entities;
        if (db.Database.IsSqlite())
        {
            // Table identifiers come from the EF model; dates are bound parameters, never SQL text.
            var table = entities.EntityType.GetTableName()!;
            var conditions = new List<string>();
            var parameters = new List<object>();
            if (from.HasValue)
            {
                conditions.Add($"date([CreatedAt]) >= {{{parameters.Count}}}");
                parameters.Add(from.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (to.HasValue)
            {
                conditions.Add($"date([CreatedAt]) <= {{{parameters.Count}}}");
                parameters.Add(to.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
            }
            if (conditions.Count > 0)
                query = entities.FromSql(System.Runtime.CompilerServices.FormattableStringFactory.Create($"SELECT * FROM [{table}] WHERE {string.Join(" AND ", conditions)}", parameters.ToArray()));
        }
        else
        {
            if (from.HasValue)
                query = query.Where(x => EF.Property<DateTimeOffset>(x, "CreatedAt") >= new DateTimeOffset(from.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
            if (to.HasValue && to.Value < DateOnly.MaxValue)
                query = query.Where(x => EF.Property<DateTimeOffset>(x, "CreatedAt") < new DateTimeOffset(to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero));
        }
        return query;
    }

    /// <summary>Inspect synthetic account balances for an integration test or onboarding.</summary>
    [HttpGet("accounts")]
    public async Task<IActionResult> Accounts(string? municipality, int page = 1, int pageSize = 100, CancellationToken ct = default)
    {
        var offset = Rules.PageOffset(page, pageSize);
        var q = db.Accounts.AsNoTracking();
        if (municipality != null)
            q = q.Where(x => x.Municipality.Code == Rules.Code(municipality));
        return Ok(new
        {
            total = await q.CountAsync(ct),
            page,
            pageSize,
            items = await q.OrderBy(x => x.Id).Skip(offset).Take(pageSize).Select(x => new { municipalityCode = x.Municipality.Code, x.AccountNumber, paymentType = x.PaymentType.Code, x.Balance }).ToListAsync(ct)
        });
    }
}
