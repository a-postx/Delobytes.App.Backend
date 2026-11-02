using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// The delivery method a posting is shipped by, as Ozon reports it in <c>delivery_method</c>.
/// FBS postings carry a seller-side warehouse here where FBO postings report nothing useful.
/// </summary>
public sealed class OzonDeliveryMethod
{
    [JsonPropertyName("id")]
    public long? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("warehouse_id")]
    public long? WarehouseId { get; init; }

    [JsonPropertyName("warehouse")]
    public string? Warehouse { get; init; }

    [JsonPropertyName("tpl_provider_id")]
    public long? TplProviderId { get; init; }

    [JsonPropertyName("tpl_provider")]
    public string? TplProvider { get; init; }
}
