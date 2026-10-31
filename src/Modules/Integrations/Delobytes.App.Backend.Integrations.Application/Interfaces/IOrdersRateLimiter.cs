using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Identifies the pacing bucket a request belongs to: a channel, the key the channel enforces its
/// own limit against (e.g. a Wildberries API key or an Ozon Client-Id), and the API method, since
/// one channel may budget several of its methods independently.
/// </summary>
/// <param name="ChannelCode">The channel code (e.g. <c>"wildberries"</c>, <c>"ozon"</c>).</param>
/// <param name="ApiKey">The key the channel enforces its limit against.</param>
/// <param name="Method">The API method, see <see cref="OrdersRateLimiterMethods"/>.</param>
public readonly record struct RateLimitScope(string ChannelCode, string ApiKey, string Method);

/// <summary>
/// Channel-neutral pacing abstraction for orders requests. Each channel paces differently (Wildberries
/// learns its allowance from response headers per (key, method); Ozon enforces a fixed 50 requests per
/// second per key plus <c>Retry-After</c>), but every caller of an orders source needs the same three
/// operations regardless of which channel it is talking to.
/// </summary>
public interface IOrdersRateLimiter
{
    /// <summary>
    /// Attempts to reserve the next request slot for <paramref name="scope"/>. Returns a result with
    /// <c>Acquired == false</c> without any side effect when the next request is not yet due.
    /// </summary>
    Task<RateLimitAcquireResult> TryAcquireAsync(RateLimitScope scope, CancellationToken cancellationToken);

    /// <summary>
    /// Records a throttled (<c>429</c>) response and computes the next allowed instant from the
    /// server's own advice.
    /// </summary>
    /// <returns>The computed next allowed attempt instant.</returns>
    Task<DateTimeOffset> ReportRateLimitedAsync(
        RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken);

    /// <summary>
    /// Records a successful response and lets the limiter adjust its pacing accordingly.
    /// </summary>
    Task ReportSuccessAsync(RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the next allowed request instant for <paramref name="scope"/> without reserving a slot.
    /// Returns <c>null</c> when a request may be issued right away.
    /// </summary>
    Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(RateLimitScope scope, CancellationToken cancellationToken);
}
