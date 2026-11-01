using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// Settlement facts Ozon reports per posting (requires <c>with.financial_data</c> in the request).
/// Matched to <see cref="OzonPostingProduct"/> rows by <see cref="OzonPostingFinancialProduct.ProductId"/>
/// == <c>sku</c>; a product with no match keeps its financial fields null rather than zero.
/// </summary>
/// <remarks>
/// Every money member here is a bare JSON number on the wire, while the same fields under
/// <c>products[]</c> come as numeric strings. The lenient converter accepts both, so the import does
/// not depend on which of the two shapes the endpoint currently sends.
/// </remarks>
public sealed class OzonPostingFinancialData
{
    [JsonPropertyName("products")]
    public List<OzonPostingFinancialProduct>? Products { get; init; }
}

/// <summary>
/// One product's settlement facts within <see cref="OzonPostingFinancialData"/>.
/// </summary>
public sealed class OzonPostingFinancialProduct
{
    [JsonPropertyName("product_id")]
    public long? ProductId { get; init; }

    [JsonPropertyName("old_price")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? OldPrice { get; init; }

    [JsonPropertyName("price")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? Price { get; init; }

    [JsonPropertyName("payout")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? Payout { get; init; }

    [JsonPropertyName("commission")]
    public OzonCommission? Commission { get; init; }

    /// <summary>
    /// The price the buyer actually paid. Not in the mapping specification's own DTO field list,
    /// but required by its mapping rule for <c>BuyerUnitPrice</c>: without it, that rule has no
    /// source to read.
    /// </summary>
    [JsonPropertyName("customer_price")]
    public OzonMoney? CustomerPrice { get; init; }
}

/// <summary>
/// The commission Ozon charges for one financial product line.
/// </summary>
public sealed class OzonCommission
{
    [JsonPropertyName("amount")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? Amount { get; init; }

    [JsonPropertyName("percent")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? Percent { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }
}
