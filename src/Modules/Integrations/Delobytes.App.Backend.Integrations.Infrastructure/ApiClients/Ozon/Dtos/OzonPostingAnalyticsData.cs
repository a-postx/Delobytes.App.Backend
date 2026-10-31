using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// Delivery and classification facts Ozon reports per posting (requires <c>with.analytics_data</c>
/// in the request).
/// </summary>
public sealed class OzonPostingAnalyticsData
{
    [JsonPropertyName("city")]
    public string? City { get; init; }

    [JsonPropertyName("region")]
    public string? Region { get; init; }

    [JsonPropertyName("warehouse_name")]
    public string? WarehouseName { get; init; }

    [JsonPropertyName("is_legal")]
    public bool? IsLegal { get; init; }

    [JsonPropertyName("is_premium")]
    public bool? IsPremium { get; init; }

    [JsonPropertyName("delivery_type")]
    public string? DeliveryType { get; init; }
}
