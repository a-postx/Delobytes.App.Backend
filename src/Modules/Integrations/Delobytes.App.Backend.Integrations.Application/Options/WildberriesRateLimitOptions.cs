namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Adaptive pacing configuration for <see cref="Interfaces.IWildberriesRateLimiter"/>. The real
/// per-key allowance is unknown ahead of time and is learned from the API's own
/// <c>x-ratelimit-*</c> headers; these values only bound the learning process.
/// </summary>
public class WildberriesRateLimitOptions
{
    /// <summary>
    /// Starting pace, in seconds. Deliberately optimistic: the real allowance is unknown per
    /// key, and a 429 is cheap (one wasted call) while an overly slow start costs days of wall
    /// clock.
    /// </summary>
    public int InitialIntervalSeconds { get; set; } = 60;

    /// <summary>
    /// Fastest pace the limiter will ever speed up to, in seconds.
    /// </summary>
    public int MinIntervalSeconds { get; set; } = 5;

    /// <summary>
    /// Pace assumed when no headers and no configuration are available, in seconds.
    /// </summary>
    public int FallbackIntervalSeconds { get; set; } = 3600;

    /// <summary>
    /// Safety margin added on top of the server's own advice, in seconds.
    /// </summary>
    public int BackoffPadSeconds { get; set; } = 5;

    /// <summary>
    /// Consecutive successes required before speeding up one step.
    /// </summary>
    public int SpeedUpAfterSuccesses { get; set; } = 3;
}
