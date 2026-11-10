namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Adaptive pacing configuration for <see cref="Interfaces.IChannelRateLimiter"/>. The real
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

    /// <summary>
    /// Gets the per-method overrides, keyed by a <c>WildberriesApiMethodRegistry</c> method name.
    /// Every field left unset falls back to the root-level value above, so an entry may override
    /// only the settings that actually differ for that method. An absent method uses the root
    /// values unchanged, which is why this section is additive: existing configuration keeps its
    /// meaning.
    /// </summary>
    public Dictionary<string, MethodRateLimitPolicy> Methods { get; set; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Resolves the effective pacing policy for <paramref name="method"/>: the method's own
    /// overrides where present, the root-level defaults everywhere else.
    /// </summary>
    /// <param name="method">Method name, see <c>WildberriesApiMethodRegistry</c>.</param>
    /// <returns>A policy with every field populated.</returns>
    public ResolvedRateLimitPolicy ResolvePolicy(string method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (!Methods.TryGetValue(method, out MethodRateLimitPolicy? overrides) || overrides is null)
        {
            return ToResolvedPolicy(new MethodRateLimitPolicy());
        }

        return ToResolvedPolicy(overrides);
    }

    private ResolvedRateLimitPolicy ToResolvedPolicy(MethodRateLimitPolicy overrides)
    {
        return new ResolvedRateLimitPolicy
        {
            InitialIntervalSeconds = overrides.InitialIntervalSeconds ?? InitialIntervalSeconds,
            MinIntervalSeconds = overrides.MinIntervalSeconds ?? MinIntervalSeconds,
            FallbackIntervalSeconds = overrides.FallbackIntervalSeconds ?? FallbackIntervalSeconds,
            BackoffPadSeconds = overrides.BackoffPadSeconds ?? BackoffPadSeconds,
            SpeedUpAfterSuccesses = overrides.SpeedUpAfterSuccesses ?? SpeedUpAfterSuccesses,
        };
    }

    /// <summary>
    /// One method's overrides. Nullable on purpose: <c>null</c> means "inherit the root-level
    /// value", which a non-nullable <c>int</c> cannot express without an in-band sentinel such as
    /// zero (a legitimate value for several of these settings).
    /// </summary>
    public sealed record MethodRateLimitPolicy
    {
        /// <summary>
        /// Gets the starting pace override, in seconds.
        /// </summary>
        public int? InitialIntervalSeconds { get; init; }

        /// <summary>
        /// Gets the fastest pace override, in seconds.
        /// </summary>
        public int? MinIntervalSeconds { get; init; }

        /// <summary>
        /// Gets the no-headers fallback pace override, in seconds.
        /// </summary>
        public int? FallbackIntervalSeconds { get; init; }

        /// <summary>
        /// Gets the safety margin override added on top of the server's advice, in seconds.
        /// </summary>
        public int? BackoffPadSeconds { get; init; }

        /// <summary>
        /// Gets the consecutive-success count override required to speed up one step.
        /// </summary>
        public int? SpeedUpAfterSuccesses { get; init; }
    }

    /// <summary>
    /// A fully populated pacing policy: the shape the limiter works with, with no "unset" state
    /// left to interpret.
    /// </summary>
    public sealed record ResolvedRateLimitPolicy
    {
        /// <summary>
        /// Gets the starting pace, in seconds.
        /// </summary>
        public required int InitialIntervalSeconds { get; init; }

        /// <summary>
        /// Gets the fastest pace the limiter will speed up to, in seconds.
        /// </summary>
        public required int MinIntervalSeconds { get; init; }

        /// <summary>
        /// Gets the no-headers fallback pace, in seconds.
        /// </summary>
        public required int FallbackIntervalSeconds { get; init; }

        /// <summary>
        /// Gets the safety margin added on top of the server's advice, in seconds.
        /// </summary>
        public required int BackoffPadSeconds { get; init; }

        /// <summary>
        /// Gets the consecutive successes required before speeding up one step.
        /// </summary>
        public required int SpeedUpAfterSuccesses { get; init; }
    }
}
