using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// A monetary amount as Ozon reports it: a decimal amount plus its currency code.
/// </summary>
/// <remarks>
/// The amount is a <see cref="decimal"/> behind a lenient converter because Ozon is not consistent
/// about its wire shape: <c>products[].price.amount</c> arrives as a numeric string
/// (<c>"880"</c>) while <c>financial_data.products[].price</c> and <c>customer_price.amount</c>
/// arrive as bare numbers. Reading only one of the two shapes aborts the whole page.
/// </remarks>
public sealed class OzonMoney
{
    [JsonPropertyName("amount")]
    [JsonConverter(typeof(JsonNumberOrStringDecimalConverter))]
    public decimal? Amount { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }
}
