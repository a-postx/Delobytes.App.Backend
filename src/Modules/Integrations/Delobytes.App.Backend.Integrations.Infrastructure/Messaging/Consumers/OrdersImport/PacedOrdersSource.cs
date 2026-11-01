using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Issues one orders request against the run's deadline and request budget, and waits out a
/// throttled response in-process when the wait fits within the caller's tolerance. Channel-neutral:
/// the caller supplies the rate limiter method and the in-process wait tolerance per call, since
/// different channels (and even different windows of the same channel) pace differently.
/// </summary>
public sealed class PacedOrdersSource
{
    private readonly IOrdersRateLimiter _rateLimiter;
    private readonly TimeProvider _timeProvider;

    public PacedOrdersSource(IOrdersRateLimiter rateLimiter, TimeProvider timeProvider)
    {
        _rateLimiter = rateLimiter;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Returns the page, or a stop value when the run's deadline or request budget ran out or the
    /// source throttled beyond <paramref name="maxInProcessWait"/>.
    /// </summary>
    /// <param name="methodName">
    /// The orders API method being paced, see <see cref="OrdersRateLimiterMethods"/>. Combined with
    /// the connection's channel code and API key into the <see cref="RateLimitScope"/> the limiter
    /// paces against.
    /// </param>
    /// <param name="maxInProcessWait">
    /// Longest throttle the caller tolerates waiting out in-process before reporting a stop instead.
    /// </param>
    internal async Task<OrdersAttempt> GetOrdersAsync(
        OrdersImportContext context,
        OrdersRunProgress progress,
        OrdersRequest request,
        string methodName,
        TimeSpan maxInProcessWait,
        CancellationToken cancellationToken)
    {
        RateLimitScope scope = new RateLimitScope(
            context.Connection.SystemChannelTemplate.Code, context.Connection.ApiKey, methodName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset now = _timeProvider.GetUtcNow();

            if (now >= context.Deadline)
            {
                DateTimeOffset? nextAllowedAt = await _rateLimiter.GetNextRequestAllowedAtAsync(scope, cancellationToken);

                DateTimeOffset budgetTime;

                if (nextAllowedAt is not null)
                {
                    budgetTime = nextAllowedAt.Value > now ? nextAllowedAt.Value : now;
                }
                else
                {
                    budgetTime = now;
                }

                return OrdersAttempt.Stopped(StopReason.Budget, budgetTime);
            }

            if (progress.RequestsIssued >= OrdersImportConstants.MaxRequestsPerRun)
            {
                throw new InvalidOperationException($"Orders fetch exceeded the per-run request budget of {OrdersImportConstants.MaxRequestsPerRun}.");
            }

            progress.LastRequestStartedAt = now;
            progress.RequestsIssued++;

            OrdersPage page = await context.OrdersSource.GetOrdersAsync(context.Connection.Id, request, cancellationToken);

            if (!page.Throttled)
            {
                return OrdersAttempt.Received(page);
            }

            DateTimeOffset retryAt = page.RetryAt
                ?? throw new InvalidOperationException("Throttled orders page has no retry time.");
            now = _timeProvider.GetUtcNow();
            TimeSpan wait = retryAt > now ? retryAt - now : TimeSpan.Zero;

            if (wait > maxInProcessWait || retryAt >= context.Deadline)
            {
                return OrdersAttempt.Stopped(StopReason.Throttled, retryAt);
            }

            progress.InProcessWaits++;
            DateTimeOffset waitStartedAt = _timeProvider.GetUtcNow();

            await Task.Delay(wait, _timeProvider, cancellationToken);

            progress.TotalWaited += _timeProvider.GetUtcNow() - waitStartedAt;
        }
    }
}
