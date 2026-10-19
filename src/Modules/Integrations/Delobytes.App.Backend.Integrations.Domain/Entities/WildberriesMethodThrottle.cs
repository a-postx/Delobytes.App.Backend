using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Adaptive rate-limit pacing state for one (API key, Wildberries method) pair. Deliberately not
/// <see cref="Contracts.Interfaces.ITenantScoped"/>: the limit is enforced by Wildberries against
/// the raw API key, which can be shared across tenants, so pacing state must be visible and
/// enforced across the whole key regardless of which tenant's job run issues the next request.
/// </summary>
public class WildberriesMethodThrottle
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the truncated SHA-256 of the raw API key. The raw key is never stored here.
    /// </summary>
    public string KeyFingerprint { get; set; } = default!;

    /// <summary>
    /// Gets or sets the Wildberries API method this pacing state applies to.
    /// </summary>
    public WildberriesApiMethod Method { get; set; }

    /// <summary>
    /// Gets or sets the earliest instant the next request may be issued.
    /// </summary>
    public DateTimeOffset NextRequestAllowedAt { get; set; }

    /// <summary>
    /// Gets or sets the current pacing interval, adapted from headers and 429 signals.
    /// </summary>
    public int CurrentIntervalSeconds { get; set; }

    /// <summary>
    /// Gets or sets the last <c>x-ratelimit-limit</c> value reported by the API, kept for
    /// diagnostics and for pacing maths.
    /// </summary>
    public int? LastLimit { get; set; }

    /// <summary>
    /// Gets or sets the last <c>x-ratelimit-remaining</c> value reported by the API.
    /// </summary>
    public int? LastRemaining { get; set; }

    /// <summary>
    /// Gets or sets the last <c>x-ratelimit-retry</c> value reported by the API.
    /// </summary>
    public int? LastRetryAfterSeconds { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code of the last response observed for this (key, method).
    /// </summary>
    public int? LastStatusCode { get; set; }

    /// <summary>
    /// Gets or sets the window length in seconds, learned from the <c>x-ratelimit-reset</c> header
    /// of a 429. Success responses do not carry <c>reset</c>, so this is the only source for it;
    /// null until the first throttle.
    /// </summary>
    public int? ObservedWindowSeconds { get; set; }

    /// <summary>
    /// Gets or sets the count of consecutive non-429 responses; drives the cautious speed-up.
    /// </summary>
    public int SuccessStreak { get; set; }

    /// <summary>
    /// Gets or sets the date and time of the last request issued for this (key, method).
    /// </summary>
    public DateTimeOffset LastRequestAt { get; set; }
}
