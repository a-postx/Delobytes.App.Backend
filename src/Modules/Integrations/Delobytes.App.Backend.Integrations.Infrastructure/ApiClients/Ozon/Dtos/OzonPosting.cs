using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// One row of an Ozon posting list response (<c>posting/fbo/list</c> or <c>posting/fbs/list</c>):
/// one shipment, possibly carrying several products of the order it belongs to.
/// </summary>
public sealed class OzonPosting
{
    [JsonPropertyName("posting_number")]
    public string? PostingNumber { get; init; }

    [JsonPropertyName("order_id")]
    public long? OrderId { get; init; }

    [JsonPropertyName("order_number")]
    public string? OrderNumber { get; init; }

    [JsonPropertyName("status")]
    public string? Status { get; init; }

    [JsonPropertyName("substatus")]
    public string? Substatus { get; init; }

    [JsonPropertyName("cancellation")]
    public OzonCancellation? Cancellation { get; init; }

    [JsonPropertyName("products")]
    public List<OzonPostingProduct>? Products { get; init; }

    [JsonPropertyName("analytics_data")]
    public OzonPostingAnalyticsData? AnalyticsData { get; init; }

    [JsonPropertyName("financial_data")]
    public OzonPostingFinancialData? FinancialData { get; init; }

    [JsonPropertyName("delivery_method")]
    public OzonDeliveryMethod? DeliveryMethod { get; init; }

    [JsonPropertyName("delivery_schema")]
    public string? DeliverySchema { get; init; }

    [JsonPropertyName("cancel_reason_id")]
    public long? CancelReasonId { get; init; }

    [JsonPropertyName("created_at")]
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// The moment Ozon moved the posting into processing. FBS postings may omit
    /// <c>created_at</c> entirely while still reporting this, so the mapper accepts it as the
    /// order date fallback.
    /// </summary>
    [JsonPropertyName("in_process_at")]
    public DateTimeOffset? InProcessAt { get; init; }
}
