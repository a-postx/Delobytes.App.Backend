using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Services;

/// <summary>
/// Adaptive rate limiter for Wildberries API calls, paced per (API key, method) pair from the
/// API's own <c>x-ratelimit-*</c> headers. Starts optimistic and tightens only when the API
/// pushes back; see <see cref="WildberriesRateLimitOptions"/> for the exact algorithm this
/// implements.
/// </summary>
public class WildberriesRateLimiter : IWildberriesRateLimiter
{
    private readonly IWildberriesMethodThrottleRepository _repository;
    private readonly WildberriesRateLimitOptions _options;
    private readonly ILogger<WildberriesRateLimiter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WildberriesRateLimiter"/> class.
    /// </summary>
    public WildberriesRateLimiter(
        IWildberriesMethodThrottleRepository repository,
        IOptions<WildberriesRateLimitOptions> options,
        ILogger<WildberriesRateLimiter> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<bool> TryAcquireAsync(
        string apiKey,
        WildberriesApiMethod method,
        CancellationToken cancellationToken)
    {
        string fingerprint = WildberriesKeyFingerprint.Compute(apiKey);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        bool acquired = await _repository.MutateAsync(
            fingerprint,
            method,
            () => CreateDefault(now),
            throttle =>
            {
                if (throttle.NextRequestAllowedAt > now)
                {
                    return false;
                }

                throttle.NextRequestAllowedAt = now.AddSeconds(throttle.CurrentIntervalSeconds);
                throttle.LastRequestAt = now;
                return true;
            },
            cancellationToken);

        if (!acquired)
        {
            _logger.LogDebug(
                "Запрос Wildberries {Method} для отпечатка ключа {KeyFingerprint} отложен лимитером.",
                method,
                fingerprint);
        }

        return acquired;
    }

    /// <inheritdoc/>
    public async Task ReportRateLimitedAsync(
        string apiKey,
        WildberriesApiMethod method,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        string fingerprint = WildberriesKeyFingerprint.Compute(apiKey);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DateTimeOffset nextAttempt = await _repository.MutateAsync(
            fingerprint,
            method,
            () => CreateDefault(now),
            throttle =>
            {
                // §1.2 fact 3: `retry` is the server's advice for the next call; `reset`
                // describes the whole window and is honoured only as a fallback and as the
                // window length remembered for ReportSuccessAsync's pace derivation.
                int wait = signal.RetrySeconds ?? signal.ResetSeconds ?? throttle.CurrentIntervalSeconds;

                throttle.NextRequestAllowedAt = now.AddSeconds(wait + _options.BackoffPadSeconds);
                throttle.CurrentIntervalSeconds = Math
                    .Min(Math.Max(throttle.CurrentIntervalSeconds, wait), _options.FallbackIntervalSeconds);
                throttle.SuccessStreak = 0;
                throttle.LastLimit = signal.Limit;
                throttle.LastRemaining = signal.Remaining;
                throttle.LastRetryAfterSeconds = signal.RetrySeconds;
                throttle.LastStatusCode = 429;
                throttle.LastRequestAt = now;

                if (signal.ResetSeconds.HasValue)
                {
                    throttle.ObservedWindowSeconds = signal.ResetSeconds;
                }

                return throttle.NextRequestAllowedAt;
            },
            cancellationToken);

        _logger.LogWarning(
            "Wildberries вернул 429 для {Method} (отпечаток ключа {KeyFingerprint}, x-request-id {RequestId}): " +
            "limit={Limit}, remaining={Remaining}, retry={RetrySeconds}с, reset={ResetSeconds}с. " +
            "Следующая попытка не раньше {NextAttempt:O}.",
            method,
            fingerprint,
            signal.RequestId,
            signal.Limit,
            signal.Remaining,
            signal.RetrySeconds,
            signal.ResetSeconds,
            nextAttempt);
    }

    /// <inheritdoc/>
    public Task ReportSuccessAsync(
        string apiKey,
        WildberriesApiMethod method,
        RateLimitSignal signal,
        CancellationToken cancellationToken)
    {
        string fingerprint = WildberriesKeyFingerprint.Compute(apiKey);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return _repository.MutateAsync(
            fingerprint,
            method,
            () => CreateDefault(now),
            throttle =>
            {
                throttle.LastLimit = signal.Limit;
                throttle.LastRemaining = signal.Remaining;
                throttle.LastStatusCode = 200;
                throttle.LastRequestAt = now;

                if (signal.Remaining == 0)
                {
                    // Window length unknown on this path (no `reset` on a 200); leave pacing as
                    // set by TryAcquireAsync and let the next call either succeed or produce the
                    // 429 that supplies `retry`/`reset`.
                    return true;
                }

                // §1.2 fact 4: a 200 never carries `reset`; derive the sustainable pace from the
                // window length remembered from an earlier 429 instead of waiting for another one.
                if (signal.Limit.HasValue && signal.Limit.Value > 0 && throttle.ObservedWindowSeconds.HasValue)
                {
                    int derived = Math.Max(
                        _options.MinIntervalSeconds,
                        throttle.ObservedWindowSeconds.Value / signal.Limit.Value);

                    throttle.CurrentIntervalSeconds = Math.Max(
                        _options.MinIntervalSeconds,
                        Math.Min(throttle.CurrentIntervalSeconds, derived));

                    return true;
                }

                throttle.SuccessStreak++;

                if (throttle.SuccessStreak >= _options.SpeedUpAfterSuccesses)
                {
                    throttle.CurrentIntervalSeconds = Math.Max(
                        _options.MinIntervalSeconds,
                        (int)(throttle.CurrentIntervalSeconds * 0.8));
                    throttle.SuccessStreak = 0;
                }

                return true;
            },
            cancellationToken);
    }

    private WildberriesMethodThrottle CreateDefault(DateTimeOffset now)
    {
        return new WildberriesMethodThrottle
        {
            NextRequestAllowedAt = now,
            CurrentIntervalSeconds = _options.InitialIntervalSeconds,
            SuccessStreak = 0,
            LastRequestAt = now
        };
    }
}
