namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Outcome of <see cref="Interfaces.IWildberriesRateLimiter.TryAcquireAsync"/>.
/// </summary>
public sealed record RateLimitAcquireResult
{
    /// <summary>
    /// Gets a value indicating whether the slot was reserved.
    /// </summary>
    public bool Acquired { get; init; }

    /// <summary>
    /// Gets the next instant a request may be issued: the newly reserved slot when
    /// <see cref="Acquired"/> is <c>true</c>, or the stored slot the caller must wait for
    /// when <c>false</c>.
    /// </summary>
    public DateTimeOffset NextRequestAllowedAt { get; init; }
}
