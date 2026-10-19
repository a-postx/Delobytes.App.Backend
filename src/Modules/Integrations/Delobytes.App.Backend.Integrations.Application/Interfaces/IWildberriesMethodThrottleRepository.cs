using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for WildberriesMethodThrottle (adaptive rate-limit pacing state per
/// API key fingerprint and method). Not tenant-scoped: lookups are keyed by the key fingerprint,
/// never by the ambient tenant.
/// </summary>
public interface IWildberriesMethodThrottleRepository
{
    /// <summary>
    /// Finds the pacing state for the given key fingerprint and method.
    /// </summary>
    public Task<WildberriesMethodThrottle?> GetAsync(
        string keyFingerprint,
        WildberriesApiMethod method,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the pacing state if none exists for its (KeyFingerprint, Method), otherwise
    /// updates the existing one.
    /// </summary>
    public Task UpsertAsync(WildberriesMethodThrottle throttle, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically loads the pacing row for (<paramref name="keyFingerprint"/>, <paramref name="method"/>),
    /// creating it via <paramref name="createDefault"/> when none exists yet, applies
    /// <paramref name="mutate"/> to it, and persists the result — all serialized so that two
    /// concurrent callers for the same (key, method) pair can never interleave their
    /// read-modify-write, including the race to create the very first row.
    /// </summary>
    public Task<TResult> MutateAsync<TResult>(
        string keyFingerprint,
        WildberriesApiMethod method,
        Func<WildberriesMethodThrottle> createDefault,
        Func<WildberriesMethodThrottle, TResult> mutate,
        CancellationToken cancellationToken);
}
