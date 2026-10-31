using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// One product line within an Ozon FBO posting.
/// </summary>
public sealed class OzonPostingProduct
{
    [JsonPropertyName("sku")]
    public long Sku { get; init; }

    [JsonPropertyName("offer_id")]
    public string? OfferId { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("price")]
    public OzonMoney? Price { get; init; }
}
