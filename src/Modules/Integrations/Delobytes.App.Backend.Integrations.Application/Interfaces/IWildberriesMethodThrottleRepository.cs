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
    Task<WildberriesMethodThrottle?> GetAsync(
        string keyFingerprint,
        WildberriesApiMethod method,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the pacing state if none exists for its (KeyFingerprint, Method), otherwise
    /// updates the existing one.
    /// </summary>
    Task UpsertAsync(WildberriesMethodThrottle throttle, CancellationToken cancellationToken);
}
