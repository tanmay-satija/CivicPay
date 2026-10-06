using System.Globalization;
namespace CivicPay.Application;

public static class Rules
{
    public static readonly string[] PaymentTypes = ["PropertyTax", "Utility", "ParkingTicket", "BusinessLicence", "Permit"];
    public static string Code(string? value) => (value ?? "").Trim().ToUpperInvariant();
    public static void Require(bool condition, string code, string message, int status = 422)
    {
        if (!condition)
            throw new BusinessException(code, message, status);
    }
    public static int PageOffset(int page, int pageSize)
    {
        var offset = ((long)page - 1) * pageSize;
        Require(page > 0 && pageSize is > 0 and <= 200 && offset <= int.MaxValue,
            "INVALID_PAGINATION", "Page must be positive, page size 1–200, and offset within range.", 400);
        return (int)offset;
    }
    public static bool Money(decimal amount) => decimal.Round(amount, 2) == amount && amount >= -9999999999.99m && amount <= 9999999999.99m;
    public static void Configuration(ConfigurationRequest r)
    {
        Require(!string.IsNullOrWhiteSpace(r.MunicipalityCode) && r.MunicipalityCode.Length <= 40 && System.Text.RegularExpressions.Regex.IsMatch(r.MunicipalityCode, @"\A[A-Za-z0-9-]+\z"), "INVALID_CONFIGURATION", "Municipality code must contain 1–40 letters, digits or hyphens.");
        Require(!string.IsNullOrWhiteSpace(r.MunicipalityName) && r.MunicipalityName.Length <= 120, "INVALID_CONFIGURATION", "A municipality name of up to 120 characters is required.");
        Require(r.Currency == "CAD", "INVALID_CONFIGURATION", "The synthetic demo supports CAD only.");
        Require(r.MinimumPayment > 0 && Money(r.MinimumPayment), "INVALID_CONFIGURATION", "Minimum payment must be positive with at most two decimal places.");
        Require(r.AcceptedPaymentTypes is { Length: > 0 } && r.AcceptedPaymentTypes.Distinct().Count() == r.AcceptedPaymentTypes.Length && r.AcceptedPaymentTypes.All(PaymentTypes.Contains), "INVALID_CONFIGURATION", "Choose unique payment types from the supported catalogue.");
    }
    public static void PaymentShape(PaymentRequest r, DateOnly today)
    {
        Require(!string.IsNullOrWhiteSpace(r.MunicipalityCode) && r.MunicipalityCode.Length <= 40 && !string.IsNullOrWhiteSpace(r.AccountNumber) && r.AccountNumber.Length <= 60 && !string.IsNullOrWhiteSpace(r.ExternalReference) && r.ExternalReference.Length <= 100 && !string.IsNullOrWhiteSpace(r.PaymentType), "REQUIRED_FIELDS", "Municipality, account, payment type and external reference are required within their documented lengths.");
        Require(r.Amount > 0 && Money(r.Amount), "INVALID_AMOUNT", "Amount must be positive, within range and have at most two decimal places.");
        Require(r.TransactionDate >= new DateOnly(2000, 1, 1) && r.TransactionDate <= today, "INVALID_DATE", "Transaction date must be between 2000-01-01 and today (UTC).");
    }
    public static void PaymentBalance(decimal amount, decimal balance, decimal minimum, bool allowPartial)
    {
        Require(amount >= minimum, "BELOW_MINIMUM", "Amount is below the municipality minimum payment.");
        Require(amount <= balance, "OVERPAYMENT", "Amount exceeds the outstanding account balance.");
        Require(allowPartial || amount == balance, "PARTIAL_PAYMENT_NOT_ALLOWED", "This municipality requires payment of the full outstanding balance.");
    }
    public static decimal ParseMoney(string value)
    {
        Require(decimal.TryParse(value, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var amount) && Money(amount), "INVALID_AMOUNT", "Use an ungrouped decimal amount with at most two decimal places.");
        return amount;
    }
}
