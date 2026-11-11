using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;

/// <summary>
/// Reserves a pacing slot for one request, waiting in-process while the limiter's next slot is
/// within the caller's tolerance. A refusal that cannot be waited out is returned to the caller,
/// which decides whether it becomes a deferred page or ends the run.
/// </summary>
internal sealed class WildberriesRequestPacer
{
    /// <summary>
    /// How many times a refusal that points at an already-due slot is re-asked before it is
    /// treated as final. A refusal of that shape is a clock-skew artefact between the limiter's
    /// own read and this process's later read, not a real absence of a slot, so the normal case
    /// succeeds on the first re-ask; the bound only stops a broken limiter from spinning.
    /// </summary>
    private const int MaxStaleSlotRetries = 5;

    private readonly IChannelRateLimiter _rateLimiter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;

    public WildberriesRequestPacer(
        IChannelRateLimiter rateLimiter,
        TimeProvider timeProvider,
        ILogger logger)
    {
        _rateLimiter = rateLimiter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Returns <c>null</c> once a slot is held, or the refusal when waiting that long is not
    /// acceptable.
    /// </summary>
    /// <param name="scope">Rate-limit scope of the request.</param>
    /// <param name="tolerance">Longest in-process wait the caller accepts.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<RateLimitAcquireResult?> WaitForSlotAsync(
        RateLimitScope scope,
        TimeSpan tolerance,
        CancellationToken cancellationToken)
    {
        int staleRefusals = 0;

        RateLimitAcquireResult result = await _rateLimiter.TryAcquireAsync(scope, cancellationToken);

        while (!result.Acquired)
        {
            TimeSpan wait = result.NextRequestAllowedAt - _timeProvider.GetUtcNow();

            if (wait <= TimeSpan.Zero)
            {
                // A refusal whose slot is already due is not a refusal: the limiter read the clock
                // before the deadline and this process read it after, which one database round-trip
                // is enough to cause. Re-asking is the only correct move here; reporting a refusal
                // would end the whole products run on a slot that is available right now.
                if (staleRefusals >= MaxStaleSlotRetries)
                {
                    return result;
                }

                staleRefusals++;
                result = await _rateLimiter.TryAcquireAsync(scope, cancellationToken);
                continue;
            }

            staleRefusals = 0;

            if (wait > tolerance)
            {
                return result;
            }

            _logger.LogDebug(
                "Wildberries {Method} request waits {WaitSeconds:F1}s in-process for the next rate-limiter slot.",
                scope.Method,
                wait.TotalSeconds);

            await Task.Delay(wait, _timeProvider, cancellationToken);

            result = await _rateLimiter.TryAcquireAsync(scope, cancellationToken);
        }

        return null;
    }
}
