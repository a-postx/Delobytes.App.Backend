namespace Delobytes.App.Backend.Finance.Application.Attribution;

/// <summary>
/// The five order-line facts attribution matches against. Carrying only these keeps a diagnostics
/// read from materialising whole <c>OrderLineRef</c> entities.
/// </summary>
/// <param name="OrderLineId">Order line identifier the entry is attributed to.</param>
/// <param name="ChannelId">Sales channel the line belongs to.</param>
/// <param name="ExternalOrderId">Channel order identity.</param>
/// <param name="ExternalLineId">Channel line identity; empty when the channel has none.</param>
/// <param name="ExternalProductId">Channel product identity.</param>
public sealed record AttributionCandidate(
    Guid OrderLineId,
    Guid ChannelId,
    string ExternalOrderId,
    string? ExternalLineId,
    string ExternalProductId);
