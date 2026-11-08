using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Issues one orders request against the run's budget, and waits out a throttled response
/// in-process when the wait fits within the run's tolerance. Channel-neutral: the method that
/// selects the pacing bucket and the wait tolerance are properties of the run's tuning.
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
    /// Returns the page, or a stop value when the run's time budget or request budget ran out, or
    /// the source throttled beyond the run's in-process wait tolerance.
    /// </summary>
    internal async Task<PageAttempt> GetOrdersAsync(
        OrdersImportContext context,
        RunCounters counters,
        OrdersRequest request,
        CancellationToken cancellationToken)
    {
        RunBudget budget = new RunBudget(context.Deadline, context.Tuning.MaxRequestsPerRun);

        RateLimitScope scope = new RateLimitScope(
            context.Connection.SystemChannelTemplate.Code, context.Connection.ApiKey, context.Tuning.MethodName);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset now = _timeProvider.GetUtcNow();

            if (now >= budget.Deadline)
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

                return new PageAttempt.Stop
                {
                    Cause = StopCause.TimeBudget,
                    ResumeAt = budgetTime,
                };
            }

            if (counters.RequestsIssued >= budget.MaxRequestsPerRun)
            {
                // A soft stop, not a crash: the run records its checkpoint and schedules a
                // continuation instead of failing on a limit the operator configured.
                return new PageAttempt.Stop
                {
                    Cause = StopCause.RequestBudget,
                    ResumeAt = now,
                };
            }

            counters.LastRequestStartedAt = now;
            counters.RequestsIssued++;

            OrdersPage page = await context.OrdersSource.GetOrdersAsync(context.Connection.Id, request, cancellationToken);

            if (!page.Throttled)
            {
                return new PageAttempt.Page
                {
                    Value = page,
                };
            }

            DateTimeOffset retryAt = page.RetryAt
                ?? throw new InvalidOperationException("Throttled orders page has no retry time.");
            now = _timeProvider.GetUtcNow();
            TimeSpan wait = retryAt > now ? retryAt - now : TimeSpan.Zero;

            if (wait > context.Tuning.MaxInProcessWait || retryAt >= budget.Deadline)
            {
                return new PageAttempt.Stop
                {
                    Cause = StopCause.Throttle,
                    ResumeAt = retryAt,
                };
            }

            counters.InProcessWaits++;
            DateTimeOffset waitStartedAt = _timeProvider.GetUtcNow();

            await Task.Delay(wait, _timeProvider, cancellationToken);

            counters.TotalWaited += _timeProvider.GetUtcNow() - waitStartedAt;
        }
    }
}
