using CivicPay.Application;
using CivicPay.Domain;
using Microsoft.EntityFrameworkCore;

namespace CivicPay.Infrastructure;

public class PaymentService(CivicPayDbContext db, TimeProvider clock) : IPaymentService
{
    public async Task<PaymentResponse> AcceptAsync(PaymentRequest raw, Guid? batchId, CancellationToken ct)
    {
        var r = raw with
        {
            MunicipalityCode = Rules.Code(raw.MunicipalityCode),
            AccountNumber = Rules.Code(raw.AccountNumber),
            ExternalReference = (raw.ExternalReference ?? "").Trim()
        };
        Rules.PaymentShape(r, DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime));
        for (var attempt = 0; attempt < 3; attempt++)
        {
            db.ChangeTracker.Clear();
            var m = await db.Municipalities.Include(x => x.Configuration).Include(x => x.PaymentTypes).ThenInclude(x => x.PaymentType).SingleOrDefaultAsync(x => x.Code == r.MunicipalityCode, ct)
                ?? throw new BusinessException("MUNICIPALITY_NOT_FOUND", "Municipality was not found.", 404);
            var replay = await FindReplayAsync(m.Id, r, ct);
            if (replay != null)
                return replay;
            Account account;
            MunicipalityPaymentType type;
            try
            {
                type = m.PaymentTypes.SingleOrDefault(x => x.PaymentType.Code == r.PaymentType)
                    ?? throw new BusinessException("UNSUPPORTED_PAYMENT_TYPE", "This municipality does not accept this payment type.");
                account = await db.Accounts.SingleOrDefaultAsync(x => x.MunicipalityId == m.Id && x.AccountNumber == r.AccountNumber, ct)
                    ?? throw new BusinessException("ACCOUNT_NOT_FOUND", "Account was not found in this municipality.", 404);
                Rules.Require(account.PaymentTypeId == type.PaymentTypeId, "ACCOUNT_TYPE_MISMATCH", "Account belongs to a different payment type.");
                Rules.PaymentBalance(r.Amount, account.Balance, m.Configuration.MinimumPayment, m.Configuration.AllowPartialPayments);
            }
            catch (BusinessException)
            {
                // A concurrent original request can commit after the initial reference lookup.
                // Its balance/rules must not turn an identical retry into an overpayment rejection.
                replay = await FindReplayAsync(m.Id, r, ct);
                if (replay != null)
                    return replay;
                throw;
            }
            account.Balance -= r.Amount;
            account.Version = Guid.NewGuid();
            m.Configuration.Version = Guid.NewGuid();
            var payment = new PaymentTransaction { MunicipalityId = m.Id, AccountId = account.Id, PaymentTypeId = type.PaymentTypeId, Amount = r.Amount, TransactionDate = r.TransactionDate, ExternalReference = r.ExternalReference, ImportBatchId = batchId };
            db.Transactions.Add(payment);
            try
            {
                await db.SaveChangesAsync(ct);
                return new(true, payment.Id, payment.Status, false);
            }
            catch (DbUpdateException ex) when (DatabaseErrors.IsConcurrencyConflict(ex))
            {
                // SaveChanges rolls back the balance and transaction together, including its savepoint in an import.
                db.ChangeTracker.Clear();
                replay = await FindReplayAsync(m.Id, r, ct);
                if (replay != null)
                    return replay;
                if (attempt == 2)
                    throw new BusinessException("DATABASE_CONFLICT", "Payment changed concurrently. Retry with the same external reference.", 409);
            }
        }
        throw new InvalidOperationException("Payment retry loop terminated unexpectedly.");
    }

    private async Task<PaymentResponse?> FindReplayAsync(int municipalityId, PaymentRequest request, CancellationToken ct)
    {
        var existing = await db.Transactions.AsNoTracking().Include(x => x.Account).Include(x => x.PaymentType)
            .SingleOrDefaultAsync(x => x.MunicipalityId == municipalityId && x.ExternalReference == request.ExternalReference, ct);
        if (existing == null)
            return null;
        Rules.Require(existing.Account.AccountNumber == request.AccountNumber && existing.PaymentType.Code == request.PaymentType && existing.Amount == request.Amount && existing.TransactionDate == request.TransactionDate,
            "DUPLICATE_REFERENCE", "External reference already belongs to a different payment payload.", 409);
        return new(true, existing.Id, existing.Status, true);
    }
}
