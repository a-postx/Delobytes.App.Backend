using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Issues one orders request against the run's deadline and request budget, and waits out a
/// throttled response in-process when the wait fits within the run's tolerance.
/// </summary>
public sealed class PacedOrdersSource
{
    private readonly IWildberriesRateLimiter _rateLimiter;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _maxInProcessWait;

    public PacedOrdersSource(
        IWildberriesRateLimiter rateLimiter,
        TimeProvider timeProvider,
        IOptions<WildberriesImportOptions> options)
    {
        WildberriesImportOptions importOptions = options.Value;

        _rateLimiter = rateLimiter;
        _timeProvider = timeProvider;
        _maxInProcessWait = TimeSpan.FromSeconds(importOptions.MaxInProcessWaitSeconds >= 0
            ? importOptions.MaxInProcessWaitSeconds
            : WildberriesImportOptions.DefaultMaxInProcessWaitSeconds);
    }

    /// <summary>
    /// Returns the page, or a stop value when the run's deadline or request budget ran out or the
    /// source throttled beyond what the run can wait out.
    /// </summary>
    internal async Task<OrdersAttempt> GetOrdersAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersRequest request,
        OrdersImportRunState state,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DateTimeOffset now = _timeProvider.GetUtcNow();

            if (now >= state.Deadline)
            {
                DateTimeOffset? nextAllowedAt = await _rateLimiter.GetNextRequestAllowedAtAsync(
                    connection.ApiKey, WildberriesApiMethod.SupplierOrders, cancellationToken);

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

            if (state.RequestsIssued >= OrdersImportConstants.MaxRequestsPerRun)
            {
                throw new InvalidOperationException($"Orders fetch exceeded the per-run request budget of {OrdersImportConstants.MaxRequestsPerRun}.");
            }

            state.LastRequestStartedAt = now;
            state.RequestsIssued++;

            OrdersPage page = await ordersSource.GetOrdersAsync(connection.Id, request, cancellationToken);

            if (!page.Throttled)
            {
                return OrdersAttempt.Received(page);
            }

            DateTimeOffset retryAt = page.RetryAt
                ?? throw new InvalidOperationException("Throttled orders page has no retry time.");
            now = _timeProvider.GetUtcNow();
            TimeSpan wait = retryAt > now ? retryAt - now : TimeSpan.Zero;

            if (wait > _maxInProcessWait || retryAt >= state.Deadline)
            {
                return OrdersAttempt.Stopped(StopReason.Throttled, retryAt);
            }

            state.InProcessWaits++;
            DateTimeOffset waitStartedAt = _timeProvider.GetUtcNow();

            await Task.Delay(wait, _timeProvider, cancellationToken);

            state.TotalWaited += _timeProvider.GetUtcNow() - waitStartedAt;
        }
    }
}
