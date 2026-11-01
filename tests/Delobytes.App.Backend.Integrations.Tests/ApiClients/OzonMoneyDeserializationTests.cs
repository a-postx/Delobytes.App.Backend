using System;
using System.Text.Json;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for the lenient decimal reader used by the Ozon money DTOs, exercised through the real
/// contracts (<see cref="OzonMoney"/>, <see cref="OzonPostingFinancialProduct"/>). Ozon sends the same
/// amount as a bare number in one block and as a numeric string in another, so both shapes have to
/// read identically — and a shape that is neither must fail loudly instead of becoming null.
/// </summary>
public class OzonMoneyDeserializationTests
{
    private static readonly JsonSerializerOptions Options = new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private static OzonMoney ReadMoney(string json)
    {
        OzonMoney? money = JsonSerializer.Deserialize<OzonMoney>(json, Options);

        money.Should().NotBeNull();
        return money!;
    }

    [Theory]
    [InlineData("{\"amount\":880}", 880)]
    [InlineData("{\"amount\":792.32}", 792.32)]
    [InlineData("{\"amount\":0}", 0)]
    [InlineData("{\"amount\":-15.75}", -15.75)]
    [InlineData("{\"amount\":\"880\"}", 880)]
    [InlineData("{\"amount\":\"792.32\"}", 792.32)]
    [InlineData("{\"amount\":\"-15.75\"}", -15.75)]
    public void Deserialize_AmountAsNumberOrNumericString_ReadsTheSameValue(string json, double expected)
    {
        OzonMoney money = ReadMoney(json);

        money.Amount.Should().Be(Convert.ToDecimal(expected));
    }

    [Fact]
    public void Deserialize_AmountInExponentNotation_IsRead()
    {
        ReadMoney("{\"amount\":\"1.5e2\"}").Amount.Should().Be(150m);
    }

    [Fact]
    public void Deserialize_NumberAndStringShapesOfTheSameAmount_AreIndistinguishable()
    {
        OzonMoney fromNumber = ReadMoney("{\"amount\":792.32,\"currency\":\"RUB\"}");
        OzonMoney fromString = ReadMoney("{\"amount\":\"792.32\",\"currency\":\"RUB\"}");

        fromNumber.Amount.Should().Be(fromString.Amount);
    }

    [Theory]
    [InlineData("{\"amount\":null}")]
    [InlineData("{\"amount\":\"\"}")]
    [InlineData("{\"amount\":\"   \"}")]
    [InlineData("{}")]
    public void Deserialize_AbsentOrEmptyAmount_IsNull(string json)
    {
        ReadMoney(json).Amount.Should().BeNull();
    }

    [Theory]
    [InlineData("{\"amount\":\"880,00\"}")]
    [InlineData("{\"amount\":\"1 200,50\"}")]
    [InlineData("{\"amount\":\"abc\"}")]
    [InlineData("{\"amount\":true}")]
    [InlineData("{\"amount\":[1]}")]
    [InlineData("{\"amount\":{}}")]
    public void Deserialize_UnexpectedAmountShape_ThrowsInsteadOfBecomingNull(string json)
    {
        Action act = () => ReadMoney(json);

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_AmountBeyondDecimalRange_Throws()
    {
        Action act = () => ReadMoney("{\"amount\":1e40}");

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Deserialize_CommissionPercentAsNumberOrString_ReadsTheSameValue()
    {
        const string withNumber = "{\"product_id\":111,\"commission\":{\"amount\":87.68,\"percent\":9.96}}";
        const string withString = "{\"product_id\":111,\"commission\":{\"amount\":\"87.68\",\"percent\":\"9.96\"}}";

        OzonPostingFinancialProduct numeric = JsonSerializer.Deserialize<OzonPostingFinancialProduct>(withNumber, Options)!;
        OzonPostingFinancialProduct textual = JsonSerializer.Deserialize<OzonPostingFinancialProduct>(withString, Options)!;

        numeric.Commission!.Percent.Should().Be(9.96m);
        textual.Commission!.Percent.Should().Be(9.96m);
        textual.Commission.Amount.Should().Be(numeric.Commission.Amount);
    }

    [Fact]
    public void Deserialize_AllFinancialMoneyMembersAsNumbers_PopulatesEveryField()
    {
        const string json = """
        {
            "product_id": 5918369809,
            "old_price": 1200.50,
            "price": 880.00,
            "payout": 792.32,
            "commission": { "amount": 87.68, "percent": 9.96, "currency": "RUB" },
            "customer_price": { "amount": 880.00, "currency": "RUB" }
        }
        """;

        OzonPostingFinancialProduct financial =
            JsonSerializer.Deserialize<OzonPostingFinancialProduct>(json, Options)!;

        financial.ProductId.Should().Be(5918369809);
        financial.OldPrice.Should().Be(1200.50m);
        financial.Price.Should().Be(880.00m);
        financial.Payout.Should().Be(792.32m);
        financial.Commission!.Amount.Should().Be(87.68m);
        financial.Commission.Percent.Should().Be(9.96m);
        financial.CustomerPrice!.Amount.Should().Be(880.00m);
    }

    [Fact]
    public void Deserialize_AllFinancialMoneyMembersAsStrings_PopulatesEveryField()
    {
        const string json = """
        {
            "product_id": 5918369809,
            "old_price": "1200.50",
            "price": "880.00",
            "payout": "792.32",
            "commission": { "amount": "87.68", "percent": "9.96", "currency": "RUB" },
            "customer_price": { "amount": "880.00", "currency": "RUB" }
        }
        """;

        OzonPostingFinancialProduct financial =
            JsonSerializer.Deserialize<OzonPostingFinancialProduct>(json, Options)!;

        financial.OldPrice.Should().Be(1200.50m);
        financial.Price.Should().Be(880.00m);
        financial.Payout.Should().Be(792.32m);
        financial.Commission!.Amount.Should().Be(87.68m);
        financial.CustomerPrice!.Amount.Should().Be(880.00m);
    }

    [Fact]
    public void Deserialize_MoneySerializedBack_WritesABareNumberNotAString()
    {
        OzonMoney money = ReadMoney("{\"amount\":\"792.32\",\"currency\":\"RUB\"}");

        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(money, Options));

        document.RootElement.GetProperty("amount").ValueKind.Should().Be(JsonValueKind.Number);
        document.RootElement.GetProperty("amount").GetDecimal().Should().Be(792.32m);
    }
}
