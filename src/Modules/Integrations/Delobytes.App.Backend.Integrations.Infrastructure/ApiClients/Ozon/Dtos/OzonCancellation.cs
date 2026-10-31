using System.Text.Json.Serialization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon.Dtos;

/// <summary>
/// Cancellation details of one Ozon FBO posting, present only when the posting was cancelled.
/// </summary>
public sealed class OzonCancellation
{
    [JsonPropertyName("cancel_reason")]
    public string? CancelReason { get; init; }

    [JsonPropertyName("cancel_reason_id")]
    public long? CancelReasonId { get; init; }

    [JsonPropertyName("cancellation_type")]
    public string? CancellationType { get; init; }

    [JsonPropertyName("cancellation_initiator")]
    public string? CancellationInitiator { get; init; }
}
