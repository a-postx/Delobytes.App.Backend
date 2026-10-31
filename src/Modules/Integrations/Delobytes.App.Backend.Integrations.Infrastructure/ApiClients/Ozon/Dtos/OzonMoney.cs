using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// A monetary amount as Ozon reports it: the decimal value as a string (to avoid floating-point
/// precision loss) plus its currency code.
/// </summary>
public sealed class OzonMoney
{
    [JsonPropertyName("amount")]
    public string? Amount { get; init; }

    [JsonPropertyName("currency")]
    public string? Currency { get; init; }
}
