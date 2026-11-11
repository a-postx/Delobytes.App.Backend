using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Channel-neutral pacing contract: one instance per channel, keyed in DI by the channel code.
/// Each channel paces differently (Wildberries learns its allowance from response headers per
/// (key, method) and persists it; Ozon enforces a fixed budget per key in memory plus
/// <c>Retry-After</c>), but every caller needs the same four operations regardless of channel.
/// </summary>
/// <remarks>
/// The interface deliberately carries no algorithm and no ordering notion: pacing state, scope
/// and signal types are all channel-agnostic. Per-channel implementations are selected by DI key,
/// never by the static type, so a channel that needs a different algorithm adds no interface.
/// </remarks>
public interface IChannelRateLimiter
{
    /// <summary>
    /// Attempts to reserve the next request slot for <paramref name="scope"/>. Returns a result with
    /// <c>Acquired == false</c> without any side effect when the next request is not yet due.
    /// </summary>
    public Task<RateLimitAcquireResult> TryAcquireAsync(RateLimitScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Records a throttled (<c>429</c>) response and computes the next allowed instant from the
    /// server's own advice.
    /// </summary>
    /// <returns>The computed next allowed attempt instant.</returns>
    public Task<DateTimeOffset> ReportRateLimitedAsync(
        RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful response and lets the limiter adjust its pacing accordingly.
    /// </summary>
    public Task ReportSuccessAsync(RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the next allowed request instant for <paramref name="scope"/> without reserving a slot.
    /// Returns <c>null</c> when a request may be issued right away.
    /// </summary>
    public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(RateLimitScope scope, CancellationToken cancellationToken);
}
