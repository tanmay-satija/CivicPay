namespace CivicPay.Domain;

public class Municipality
{
    public int Id
    {
        get; set;
    }
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public MunicipalityConfiguration Configuration { get; set; } = new();
    public List<MunicipalityPaymentType> PaymentTypes { get; set; } = [];
}
public class MunicipalityConfiguration
{
    public int MunicipalityId
    {
        get; set;
    }
    public string Currency { get; set; } = "CAD";
    public bool AllowPartialPayments
    {
        get; set;
    }
    public decimal MinimumPayment
    {
        get; set;
    }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class PaymentType
{
    public int Id
    {
        get; set;
    }
    public string Code { get; set; } = "";
}
public class MunicipalityPaymentType
{
    public int MunicipalityId
    {
        get; set;
    }
    public int PaymentTypeId
    {
        get; set;
    }
    public PaymentType PaymentType { get; set; } = null!;
}
public class Account
{
    public int Id
    {
        get; set;
    }
    public int MunicipalityId
    {
        get; set;
    }
    public Municipality Municipality { get; set; } = null!;
    public string AccountNumber { get; set; } = "";
    public int PaymentTypeId
    {
        get; set;
    }
    public PaymentType PaymentType { get; set; } = null!;
    public decimal Balance
    {
        get; set;
    }
    public Guid Version { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class PaymentTransaction
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int MunicipalityId
    {
        get; set;
    }
    public Municipality Municipality { get; set; } = null!;
    public int AccountId
    {
        get; set;
    }
    public Account Account { get; set; } = null!;
    public int PaymentTypeId
    {
        get; set;
    }
    public PaymentType PaymentType { get; set; } = null!;
    public decimal Amount
    {
        get; set;
    }
    public DateOnly TransactionDate
    {
        get; set;
    }
    public string ExternalReference { get; set; } = "";
    public string Status { get; set; } = "Accepted";
    public Guid? ImportBatchId
    {
        get; set;
    }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class ImportBatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Kind { get; set; } = "payments";
    public int ExpectedRecordCount
    {
        get; set;
    }
    public string FileName { get; set; } = "";
    public string Status { get; set; } = "Processing";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt
    {
        get; set;
    }
    public List<ImportRecord> Records { get; set; } = [];
}
public class ImportRecord
{
    public long Id
    {
        get; set;
    }
    public Guid ImportBatchId
    {
        get; set;
    }
    public int RowNumber
    {
        get; set;
    }
    public string MunicipalityCode { get; set; } = "";
    public decimal? SourceAmount
    {
        get; set;
    }
    public decimal ImportedAmount
    {
        get; set;
    }
    public string Status { get; set; } = "Rejected";
    public string? ErrorCode
    {
        get; set;
    }
    public string? Message
    {
        get; set;
    }
    public Guid? TransactionId
    {
        get; set;
    }
}
public class IntegrationEvent
{
    public long Id
    {
        get; set;
    }
    public string CorrelationId { get; set; } = "";
    public string Path { get; set; } = "";
    public string Method { get; set; } = "";
    public string? MunicipalityCode
    {
        get; set;
    }
    public int StatusCode
    {
        get; set;
    }
    public double DurationMs
    {
        get; set;
    }
    public string? ErrorCode
    {
        get; set;
    }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
public class ErrorLog
{
    public long Id
    {
        get; set;
    }
    public string CorrelationId { get; set; } = "";
    public string? MunicipalityCode
    {
        get; set;
    }
    public string ErrorCode { get; set; } = "";
    public string Message { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
