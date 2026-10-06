using CivicPay.Application;
namespace CivicPay.UnitTests;

public class RulesTests
{
    [Fact] public void Extreme_decimal_is_rejected_without_overflow() => Assert.False(Rules.Money(decimal.MinValue));
    private static ConfigurationRequest Configuration() => new("TEST-CITY", "Test City", "CAD", true, 5, ["PropertyTax", "Utility"]);
    [Fact] public void Valid_configuration_is_accepted() => Rules.Configuration(Configuration());
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(0.001)]
    public void Minimum_requires_positive_cents(decimal min) => Assert.Equal("INVALID_CONFIGURATION", Assert.Throws<BusinessException>(() => Rules.Configuration(Configuration() with { MinimumPayment = min })).Code);
    [Fact] public void Duplicate_types_are_rejected() => Assert.Throws<BusinessException>(() => Rules.Configuration(Configuration() with { AcceptedPaymentTypes = ["Utility", "Utility"] }));
    [Fact] public void Unknown_type_is_rejected() => Assert.Throws<BusinessException>(() => Rules.Configuration(Configuration() with { AcceptedPaymentTypes = ["Cash"] }));
    [Fact] public void Empty_types_are_rejected() => Assert.Throws<BusinessException>(() => Rules.Configuration(Configuration() with { AcceptedPaymentTypes = [] }));
    [Fact] public void Unsupported_currency_is_rejected() => Assert.Throws<BusinessException>(() => Rules.Configuration(Configuration() with { Currency = "USD" }));
    [Theory]
    [InlineData(4, 100, 5, true, "BELOW_MINIMUM")]
    [InlineData(25, 100, 5, false, "PARTIAL_PAYMENT_NOT_ALLOWED")]
    [InlineData(101, 100, 5, true, "OVERPAYMENT")]
    public void Configured_balance_rules_are_enforced(decimal amount, decimal balance, decimal min, bool partial, string code) => Assert.Equal(code, Assert.Throws<BusinessException>(() => Rules.PaymentBalance(amount, balance, min, partial)).Code);
    [Fact] public void Full_balance_is_accepted_without_partial_payments() => Rules.PaymentBalance(100, 100, 10, false);
    [Fact] public void Partial_payment_is_accepted_when_enabled() => Rules.PaymentBalance(25, 100, 5, true);
    [Theory]
    [InlineData("12.50", 12.50)]
    [InlineData("-2.50", -2.50)]
    public void Money_parser_uses_invariant_decimals(string value, decimal expected) => Assert.Equal(expected, Rules.ParseMoney(value));
    [Theory]
    [InlineData("1,000")]
    [InlineData("CAD 5")]
    [InlineData("1.001")]
    [InlineData("NaN")]
    [InlineData("10000000000")]
    public void Invalid_money_is_rejected(string value) => Assert.Throws<BusinessException>(() => Rules.ParseMoney(value));
    [Fact]
    public void Future_date_is_rejected()
    {
        var today = new DateOnly(2026, 10, 5);
        Assert.Equal("INVALID_DATE", Assert.Throws<BusinessException>(() => Rules.PaymentShape(new("TEST-CITY", "A", "Utility", 10, today.AddDays(1), "REF"), today)).Code);
    }
    [Fact] public void Whitespace_fields_are_rejected() => Assert.Equal("REQUIRED_FIELDS", Assert.Throws<BusinessException>(() => Rules.PaymentShape(new(" ", "A", "Utility", 10, new(2026, 10, 5), "REF"), new(2026, 10, 5))).Code);
    [Fact]
    public void Csv_handles_quotes_commas_newlines_and_empty_fields()
    {
        var rows = CsvReader.Parse("a,b,c\r\n\"two, words\",\"escaped \"\"quote\"\"\",\r\n\"line\nwrap\",b,c").ToList();
        Assert.Equal(3, rows.Count);
        Assert.Equal("two, words", rows[1][0]);
        Assert.Equal("escaped \"quote\"", rows[1][1]);
        Assert.Equal("", rows[1][2]);
        Assert.Equal("line\nwrap", rows[2][0]);
    }
    [Theory]
    [InlineData("\"unfinished")]
    [InlineData("a\"quote,b")]
    [InlineData("\"closed\"x,b")]
    public void Malformed_csv_is_rejected(string text) => Assert.Equal("INVALID_CSV", Assert.Throws<BusinessException>(() => CsvReader.Parse(text).ToList()).Code);
}
