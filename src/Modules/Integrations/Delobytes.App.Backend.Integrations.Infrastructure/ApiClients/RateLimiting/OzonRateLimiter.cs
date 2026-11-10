using System.Collections.Concurrent;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;

/// <summary>
/// Paces Ozon orders requests with a per-<see cref="RateLimitScope.ApiKey"/> token bucket honouring
/// the documented 50 requests/second budget, plus a hard penalty window driven by a <c>429</c>'s
/// <c>Retry-After</c> or by a <c>Ratelimit-Remaining: 0</c> on a successful response.
/// Registered as a singleton: pacing state must survive across scoped consumers of one process.
/// Per-process only; horizontal scale-out of the service needs a distributed limiter (see Appendix C
/// of the Ozon orders import specification).
/// </summary>
public sealed class OzonRateLimiter : IChannelRateLimiter
{
    private const int Capacity = 50;
    private const double RefillTokensPerSecond = 50.0;
    private static readonly TimeSpan DefaultRetryAfter = TimeSpan.FromSeconds(1);

    private readonly ConcurrentDictionary<string, Bucket> _buckets = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _minimumRequestInterval;

    /// <summary>
    /// Initializes a new instance of the <see cref="OzonRateLimiter"/> class.
    /// </summary>
    /// <param name="timeProvider">Clock abstraction for testability.</param>
    /// <param name="minimumRequestInterval">
    /// Minimum interval enforced between consecutive requests on the same API key, across every
    /// job type (FBO and FBS). <see cref="TimeSpan.Zero"/> disables the throttle.
    /// </param>
    public OzonRateLimiter(TimeProvider timeProvider, TimeSpan minimumRequestInterval)
    {
        _timeProvider = timeProvider;
        _minimumRequestInterval = minimumRequestInterval;
    }

    /// <summary>
    /// Gets the number of buckets currently held. Exposed for tests asserting the dictionary stays
    /// proportional to active keys.
    /// </summary>
    internal int BucketCount
    {
        get
        {
            return _buckets.Count;
        }
    }

    /// <inheritdoc/>
    public Task<RateLimitAcquireResult> TryAcquireAsync(RateLimitScope scope, CancellationToken cancellationToken)
    {
        Bucket bucket = GetOrCreateBucket(scope.ApiKey);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (bucket.Lock)
        {
            bucket.LastSeenAt = now;
            if (bucket.PenaltyUntil is DateTimeOffset penaltyUntil && penaltyUntil > now)
            {
                return Task.FromResult(new RateLimitAcquireResult
                {
                    Acquired = false,
                    NextRequestAllowedAt = penaltyUntil
                });
            }

            if (_minimumRequestInterval > TimeSpan.Zero && bucket.LastRequestAt is DateTimeOffset lastRequestAt)
            {
                DateTimeOffset earliestNext = lastRequestAt + _minimumRequestInterval;

                if (earliestNext > now)
                {
                    return Task.FromResult(new RateLimitAcquireResult
                    {
                        Acquired = false,
                        NextRequestAllowedAt = earliestNext
                    });
                }
            }

            Refill(bucket, now);

            if (bucket.Tokens >= 1.0)
            {
                bucket.Tokens -= 1.0;
                bucket.LastRequestAt = now;

                return Task.FromResult(new RateLimitAcquireResult
                {
                    Acquired = true,
                    NextRequestAllowedAt = now
                });
            }

            double tokensNeeded = 1.0 - bucket.Tokens;
            TimeSpan wait = TimeSpan.FromSeconds(tokensNeeded / RefillTokensPerSecond);

            return Task.FromResult(new RateLimitAcquireResult
            {
                Acquired = false,
                NextRequestAllowedAt = now + wait
            });
        }
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset> ReportRateLimitedAsync(
        RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
    {
        Bucket bucket = GetOrCreateBucket(scope.ApiKey);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        TimeSpan retryAfter = signal.RetrySeconds is int seconds and > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultRetryAfter;

        DateTimeOffset penaltyUntil = now + retryAfter;

        lock (bucket.Lock)
        {
            bucket.LastSeenAt = now;
            bucket.PenaltyUntil = penaltyUntil;
        }

        return Task.FromResult(penaltyUntil);
    }

    /// <inheritdoc/>
    public Task ReportSuccessAsync(RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
    {
        if (signal.Remaining != 0)
        {
            return Task.CompletedTask;
        }

        Bucket bucket = GetOrCreateBucket(scope.ApiKey);
        DateTimeOffset now = _timeProvider.GetUtcNow();
        DateTimeOffset nextSecondBoundary = NextSecondBoundary(now);

        lock (bucket.Lock)
        {
            bucket.LastSeenAt = now;

            if (bucket.PenaltyUntil is null || bucket.PenaltyUntil.Value < nextSecondBoundary)
            {
                bucket.PenaltyUntil = nextSecondBoundary;
            }
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
        RateLimitScope scope, CancellationToken cancellationToken)
    {
        string fingerprint = ApiKeyFingerprint.Compute(scope.ApiKey);

        if (!_buckets.TryGetValue(fingerprint, out Bucket? bucket))
        {
            return Task.FromResult((DateTimeOffset?)null);
        }

        DateTimeOffset now = _timeProvider.GetUtcNow();

        lock (bucket.Lock)
        {
            bucket.LastSeenAt = now;
            if (bucket.PenaltyUntil is DateTimeOffset penaltyUntil && penaltyUntil > now)
            {
                return Task.FromResult((DateTimeOffset?)penaltyUntil);
            }

            if (_minimumRequestInterval > TimeSpan.Zero && bucket.LastRequestAt is DateTimeOffset lastRequestAt)
            {
                DateTimeOffset earliestNext = lastRequestAt + _minimumRequestInterval;

                if (earliestNext > now)
                {
                    return Task.FromResult((DateTimeOffset?)earliestNext);
                }
            }

            Refill(bucket, now);

            if (bucket.Tokens >= 1.0)
            {
                return Task.FromResult((DateTimeOffset?)null);
            }

            double tokensNeeded = 1.0 - bucket.Tokens;
            TimeSpan wait = TimeSpan.FromSeconds(tokensNeeded / RefillTokensPerSecond);

            return Task.FromResult((DateTimeOffset?)(now + wait));
        }
    }

    private static void Refill(Bucket bucket, DateTimeOffset now)
    {
        TimeSpan elapsed = now - bucket.LastRefillAt;

        if (elapsed <= TimeSpan.Zero)
        {
            return;
        }

        bucket.Tokens = Math.Min(Capacity, bucket.Tokens + (elapsed.TotalSeconds * RefillTokensPerSecond));
        bucket.LastRefillAt = now;
    }

    private static DateTimeOffset NextSecondBoundary(DateTimeOffset now)
    {
        long ticksIntoSecond = now.UtcTicks % TimeSpan.TicksPerSecond;

        if (ticksIntoSecond == 0)
        {
            return now;
        }

        return now.AddTicks(TimeSpan.TicksPerSecond - ticksIntoSecond);
    }

    private Bucket GetOrCreateBucket(string apiKey)
    {
        DateTimeOffset now = _timeProvider.GetUtcNow();
        string fingerprint = ApiKeyFingerprint.Compute(apiKey);

        EvictIdleBuckets(now);

        return _buckets.GetOrAdd(fingerprint, _ => new Bucket(Capacity, now));
    }

    /// <summary>
    /// Drops buckets that no request has touched for four minimum intervals (ten minutes when the
    /// throttle is disabled), so the dictionary stays proportional to the keys in active use
    /// instead of growing with every key ever seen.
    /// </summary>
    private void EvictIdleBuckets(DateTimeOffset now)
    {
        TimeSpan idleLimit = _minimumRequestInterval > TimeSpan.Zero
            ? TimeSpan.FromTicks(_minimumRequestInterval.Ticks * 4)
            : TimeSpan.FromMinutes(10);

        foreach (KeyValuePair<string, Bucket> entry in _buckets)
        {
            DateTimeOffset lastSeenAt;

            lock (entry.Value.Lock)
            {
                lastSeenAt = entry.Value.LastSeenAt;
            }

            if (now - lastSeenAt > idleLimit)
            {
                _buckets.TryRemove(entry.Key, out _);
            }
        }
    }

    private sealed class Bucket
    {
        public Bucket(double tokens, DateTimeOffset lastRefillAt)
        {
            Tokens = tokens;
            LastRefillAt = lastRefillAt;
            LastSeenAt = lastRefillAt;
        }

        public object Lock { get; } = new();

        public double Tokens { get; set; }

        public DateTimeOffset LastRefillAt { get; set; }

        public DateTimeOffset LastSeenAt { get; set; }

        public DateTimeOffset? PenaltyUntil { get; set; }

        public DateTimeOffset? LastRequestAt { get; set; }
    }
}
