using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Decides when the next call to a given (Wildberries API key, method) pair may go out, and
/// learns the real allowance from the <c>x-ratelimit-*</c> headers of the API's own responses.
/// Pacing state is scoped by key fingerprint, not by connection or tenant: the limit is enforced
/// by Wildberries against the raw key, which may be shared across tenants.
/// </summary>
public interface IWildberriesRateLimiter
{
    /// <summary>
    /// Attempts to reserve the next request slot for <paramref name="apiKey"/>/<paramref name="method"/>.
    /// Returns a result with <c>Acquired == false</c> without any side effect when the pacing
    /// state says the next request is not yet due; otherwise advances the pacing state and
    /// returns <c>Acquired == true</c>. Safe under concurrent callers for the same (key, method)
    /// pair.
    /// </summary>
    public Task<RateLimitAcquireResult> TryAcquireAsync(
        string apiKey,
        WildberriesApiMethod method,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the headers of a throttled (<c>429</c>) response and computes the next allowed
    /// instant from the server's own advice.
    /// </summary>
    /// <returns>The computed next allowed attempt instant.</returns>
    public Task<DateTimeOffset> ReportRateLimitedAsync(
        string apiKey,
        WildberriesApiMethod method,
        RateLimitSignal signal,
        CancellationToken cancellationToken);

    /// <summary>
    /// Reads the next allowed request instant for <paramref name="apiKey"/>/<paramref name="method"/>
    /// without reserving a slot or taking the advisory lock. Returns now when no pacing row exists
    /// yet.
    /// </summary>
    public Task<DateTimeOffset> GetNextRequestAllowedAtAsync(
        string apiKey,
        WildberriesApiMethod method,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the headers of a successful response and performs the cautious speed-up.
    /// </summary>
    public Task ReportSuccessAsync(
        string apiKey,
        WildberriesApiMethod method,
        RateLimitSignal signal,
        CancellationToken cancellationToken);
}
