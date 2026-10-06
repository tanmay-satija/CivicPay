namespace CivicPay.Application;

public record ConfigurationRequest(string MunicipalityCode, string MunicipalityName, string Currency, bool AllowPartialPayments, decimal MinimumPayment, string[] AcceptedPaymentTypes, Guid? Version = null);
public record ConfigurationResponse(int Id, string MunicipalityCode, string MunicipalityName, string Currency, bool AllowPartialPayments, decimal MinimumPayment, string[] AcceptedPaymentTypes, Guid Version);
public record PaymentRequest(string MunicipalityCode, string AccountNumber, string PaymentType, decimal Amount, DateOnly TransactionDate, string ExternalReference);
public record PaymentResponse(bool Success, Guid TransactionId, string Status, bool Replayed);
public record ApiError(bool Success, string ErrorCode, string Message, string? CorrelationId = null);
public sealed class BusinessException(string code, string message, int status = 422) : Exception(message)
{
    public string Code { get; } = code; public int Status { get; } = status;
}
public record AccountRequest(string MunicipalityCode, string AccountNumber, string PaymentType, decimal Balance);
public record ReconciliationResult(Guid BatchId, string Kind, int SourceRecordCount, int ImportedRecordCount, int RejectedRecordCount, int SkippedRecordCount, decimal TotalSourceAmount, decimal TotalImportedAmount, decimal Difference, bool SourceAmountComplete, string Status);
public interface IConfigurationService
{
    Task<List<ConfigurationResponse>> ListAsync(CancellationToken ct);
    Task<ConfigurationResponse> GetAsync(string code, CancellationToken ct);
    Task<ConfigurationResponse> SaveAsync(ConfigurationRequest request, bool create, CancellationToken ct);
}
public interface IPaymentService
{
    Task<PaymentResponse> AcceptAsync(PaymentRequest request, Guid? batchId, CancellationToken ct);
}
public interface IImportService
{
    Task<Guid> ImportAsync(Stream stream, string fileName, string kind, CancellationToken ct);
    Task<ReconciliationResult> ReconcileAsync(Guid batchId, CancellationToken ct);
}
