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
    /// Returns <c>false</c> without any side effect when the pacing state says the next request
    /// is not yet due; otherwise advances the pacing state and returns <c>true</c>. Safe under
    /// concurrent callers for the same (key, method) pair.
    /// </summary>
    public Task<bool> TryAcquireAsync(
        string apiKey,
        WildberriesApiMethod method,
        CancellationToken cancellationToken);

    /// <summary>
    /// Records the headers of a throttled (<c>429</c>) response and computes the next allowed
    /// instant from the server's own advice.
    /// </summary>
    public Task ReportRateLimitedAsync(
        string apiKey,
        WildberriesApiMethod method,
        RateLimitSignal signal,
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
