using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;
using CivicPay.Application;
using CivicPay.Domain;
using Microsoft.EntityFrameworkCore;
namespace CivicPay.Infrastructure;

public class ImportService(CivicPayDbContext db, IPaymentService payments, ILogger<ImportService>? logger = null) : IImportService
{
    public async Task<Guid> ImportAsync(Stream stream, string fileName, string kind, CancellationToken ct)
    {
        Rules.Require(kind is "payments" or "accounts", "INVALID_IMPORT_KIND", "Import kind must be payments or accounts.", 400);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        while ((read = await stream.ReadAsync(buffer.AsMemory(), ct)) != 0)
        {
            Rules.Require(bytes.Length + read <= 2_000_000, "IMPORT_TOO_LARGE", "CSV must not exceed 2 MB (2,000,000 bytes).", 413);
            await bytes.WriteAsync(buffer.AsMemory(0, read), ct);
        }
        string text;
        try
        {
            text = new UTF8Encoding(false, true).GetString(bytes.GetBuffer(), 0, (int)bytes.Length);
        }
        catch (DecoderFallbackException) { throw new BusinessException("INVALID_CSV_ENCODING", "CSV must contain valid UTF-8 text.", 400); }
        var rows = CsvReader.Parse(text.TrimStart('\uFEFF')).Take(5002).ToList();
        string[] header = kind == "payments" ? ["municipalityCode", "accountNumber", "paymentType", "amount", "transactionDate", "externalReference"] : ["municipalityCode", "accountNumber", "paymentType", "balance"];
        Rules.Require(rows.Count > 0 && rows[0].SequenceEqual(header), "INVALID_CSV_HEADER", "CSV header must match the documented column order.", 400);
        Rules.Require(rows.Count <= 5001, "IMPORT_TOO_LARGE", "At most 5000 data rows are permitted.", 413);
        var batch = new ImportBatch { ExpectedRecordCount = rows.Count - 1, Kind = kind, FileName = Path.GetFileName(fileName.Replace('\\', '/'))[..Math.Min(Path.GetFileName(fileName.Replace('\\', '/')).Length, 120)] };
        db.ImportBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        try
        {
            for (int i = 1; i < rows.Count; i++)
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                var row = rows[i];
                var record = new ImportRecord { ImportBatchId = batch.Id, RowNumber = i + 1, MunicipalityCode = row.Length > 0 ? Rules.Code(row[0])[..Math.Min(Rules.Code(row[0]).Length, 40)] : "" };
                if (row.Length > 3 && decimal.TryParse(row[3], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var source) && Rules.Money(source))
                    record.SourceAmount = source;
                try
                {
                    Rules.Require(row.Length == header.Length, "INVALID_CSV_ROW", "CSV row has an unexpected number of fields.");
                    var amount = Rules.ParseMoney(row[3]);
                    if (kind == "payments")
                    {
                        Rules.Require(DateOnly.TryParseExact(row[4], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date), "INVALID_DATE", "Use a valid yyyy-MM-dd transaction date.");
                        var result = await payments.AcceptAsync(new(row[0], row[1], row[2], amount, date, row[5]), batch.Id, ct);
                        record.TransactionId = result.TransactionId;
                        record.Status = result.Replayed ? "Skipped" : "Imported";
                        record.ImportedAmount = result.Replayed ? 0 : amount;
                        if (result.Replayed)
                        {
                            record.ErrorCode = "DUPLICATE_REPLAY";
                            record.Message = "Identical transaction already exists; no new amount imported.";
                        }
                    }
                    else
                    {
                        var imported = await ImportAccount(new(row[0], row[1], row[2], amount), ct);
                        record.Status = imported ? "Imported" : "Skipped";
                        record.ImportedAmount = imported ? amount : 0;
                        if (!imported)
                        {
                            record.ErrorCode = "DUPLICATE_ACCOUNT";
                            record.Message = "Identical account already exists.";
                        }
                    }
                }
                catch (BusinessException ex) { record.ErrorCode = ex.Code; record.Message = ex.Message; }
                db.ChangeTracker.Clear();
                db.ImportRecords.Add(record);
                // Commit the business change and its audit row together.
                await db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);
            }
            db.ChangeTracker.Clear();
            batch = await db.ImportBatches.FindAsync([batch.Id], ct) ?? throw new InvalidOperationException();
            batch.Status = "Completed";
            batch.CompletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);
            return batch.Id;
        }
        catch
        {
            // Preserve the original failure even if the database is unavailable during cleanup.
            try
            {
                using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                db.ChangeTracker.Clear();
                var failed = await db.ImportBatches.FindAsync([batch.Id], cleanup.Token);
                if (failed != null)
                {
                    failed.Status = "Failed";
                    failed.CompletedAt = DateTimeOffset.UtcNow;
                    await db.SaveChangesAsync(cleanup.Token);
                }
            }
            catch (Exception cleanupError) { logger?.LogError(cleanupError, "Could not mark import {BatchId} failed", batch.Id); }
            throw;
        }
    }
    private async Task<bool> ImportAccount(AccountRequest r, CancellationToken ct)
    {
        Rules.Require(!string.IsNullOrWhiteSpace(r.AccountNumber) && r.AccountNumber.Length <= 60, "REQUIRED_FIELDS", "Account number of up to 60 characters is required.");
        Rules.Require(r.Balance >= 0 && Rules.Money(r.Balance), "INVALID_AMOUNT", "Account balance must be nonnegative with at most two decimal places.");
        var m = await db.Municipalities.Include(x => x.Configuration).Include(x => x.PaymentTypes).ThenInclude(x => x.PaymentType).SingleOrDefaultAsync(x => x.Code == Rules.Code(r.MunicipalityCode), ct) ?? throw new BusinessException("MUNICIPALITY_NOT_FOUND", "Municipality was not found.", 404);
        var type = m.PaymentTypes.SingleOrDefault(x => x.PaymentType.Code == r.PaymentType) ?? throw new BusinessException("UNSUPPORTED_PAYMENT_TYPE", "This municipality does not accept this payment type.");
        var number = Rules.Code(r.AccountNumber);
        var existing = await db.Accounts.SingleOrDefaultAsync(x => x.MunicipalityId == m.Id && x.AccountNumber == number, ct);
        if (existing != null)
        {
            Rules.Require(existing.PaymentTypeId == type.PaymentTypeId && existing.Balance == r.Balance, "ACCOUNT_CONFLICT", "Account already exists with different data.", 409);
            return false;
        }
        // Account creation and payment-type changes use the same optimistic concurrency token.
        m.Configuration.Version = Guid.NewGuid();
        db.Accounts.Add(new()
        {
            MunicipalityId = m.Id,
            AccountNumber = number,
            PaymentTypeId = type.PaymentTypeId,
            Balance = r.Balance
        });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (DatabaseErrors.IsConcurrencyConflict(ex)) { throw new BusinessException("DATABASE_CONFLICT", "Account could not be saved. Inspect duplicates and retry.", 409); }
        return true;
    }
    public async Task<ReconciliationResult> ReconcileAsync(Guid id, CancellationToken ct)
    {
        var b = await db.ImportBatches.AsNoTracking().Include(x => x.Records).SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new BusinessException("BATCH_NOT_FOUND", "Import batch was not found.", 404);
        return Reconcile(b);
    }

    public static ReconciliationResult Reconcile(ImportBatch b)
    {
        var source = b.Records.Sum(x => x.SourceAmount ?? 0);
        var imported = b.Records.Sum(x => x.ImportedAmount);
        var complete = b.Records.Count == b.ExpectedRecordCount && b.Records.All(x => x.SourceAmount.HasValue);
        var rejected = b.Records.Count(x => x.Status == "Rejected");
        var skipped = b.Records.Count(x => x.Status == "Skipped");
        var status = b.Status != "Completed" || (b.Records.Count > 0 && rejected == b.Records.Count) ? "FAILED" : !complete || rejected > 0 || skipped > 0 || source != imported ? "WARNING" : "RECONCILED";
        return new(b.Id, b.Kind, b.ExpectedRecordCount, b.Records.Count(x => x.Status == "Imported"), rejected, skipped, source, imported, source - imported, complete, status);
    }
}
