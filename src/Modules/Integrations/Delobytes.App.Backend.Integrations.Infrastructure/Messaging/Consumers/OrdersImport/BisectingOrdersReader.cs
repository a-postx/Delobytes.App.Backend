using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Fetches a whole orders window, recursively bisecting it while the endpoint reports a partial
/// (capped) response.
/// </summary>
internal sealed class BisectingOrdersReader
{
    private readonly PacedOrdersSource _pacedSource;
    private readonly ILogger _logger;
    private readonly TimeSpan _minWindow;

    public BisectingOrdersReader(PacedOrdersSource pacedSource, ILogger logger, TimeSpan minWindow)
    {
        _pacedSource = pacedSource;
        _logger = logger;
        _minWindow = minWindow;
    }

    /// <summary>
    /// Fetches the requested window. A window stopped by the run's budget or by a throttle is
    /// reported as a stop with no orders: anything already read belongs to an unread window, so
    /// publishing it would advance the watermark over orders the bisection never saw.
    /// </summary>
    public async Task<OrdersWindowFetchResult> FetchAllPagesAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersRequest request,
        Guid jobRunId,
        OrdersImportRunState state,
        CancellationToken cancellationToken)
    {
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey = new Dictionary<string, ChannelOrderSnapshot>(StringComparer.Ordinal);

        state.RowsWithoutIdentity = 0;

        OrdersAttempt attempt = await FetchWindowAsync(ordersSource, connection, request, jobRunId, ordersByOrderKey, state, cancellationToken);

        if (attempt.IsStopped)
        {
            return OrdersWindowFetchResult.Stopped(
                attempt.StopReason,
                attempt.ResumeAt,
                state.RequestsIssued,
                state.RowsWithoutIdentity);
        }

        return new OrdersWindowFetchResult
        {
            Orders = ordersByOrderKey.Values.ToList(),
            RequestsIssued = state.RequestsIssued,
            RowsWithoutIdentity = state.RowsWithoutIdentity,
            StopReason = StopReason.Completed,
            ResumeAt = null,
        };
    }

    /// <summary>
    /// Fetches one window. The endpoint serves rows in ascending order of the selection instant and
    /// caps a response at a fixed row count, so a capped response proves that everything up to its
    /// last returned instant was served and leaves only the region above that instant unknown. The
    /// recursion therefore splits that unknown region at its midpoint and resumes the left half from
    /// the covered boundary, not from the requested <c>dateFrom</c>: the endpoint has no
    /// <c>dateTo</c>, so re-requesting the parent's own <c>dateFrom</c> would return the identical
    /// block, report the cap again, and never advance. Rows falling exactly on a boundary are
    /// reported by both siblings; the dedup dictionary collapses them into one order and unions
    /// their lines, so the bisection can cut through a cart without losing or duplicating a unit.
    /// </summary>
    private async Task<OrdersAttempt> FetchWindowAsync(
        IOrdersSource ordersSource,
        Connection connection,
        OrdersRequest request,
        Guid jobRunId,
        Dictionary<string, ChannelOrderSnapshot> ordersByOrderKey,
        OrdersImportRunState state,
        CancellationToken cancellationToken)
    {
        OrdersAttempt attempt = await _pacedSource.GetOrdersAsync(ordersSource, connection, request, state, cancellationToken);

        if (attempt.IsStopped)
        {
            return attempt;
        }

        OrdersPage ordersPage = attempt.Page!;

        if (ordersPage.RowsWithoutIdentity > 0)
        {
            // A page that dropped rows cannot be imported partially: the missing orders would be
            // indistinguishable from orders that never existed, and advancing the watermark past
            // this window would hide them from every later delta run as well. Failing here reuses
            // the same fail-fast stance as the unresolvable partial response below, and lets the
            // next scheduled run retry the window once the source data is complete.
            state.RowsWithoutIdentity += ordersPage.RowsWithoutIdentity;

            _logger.LogError(
                "JobRun {JobRunId}: window {From} - {To} returned {RowsWithoutIdentity} row(s) without a usable order identity, failing the run",
                jobRunId,
                request.DateFrom,
                request.DateTo,
                ordersPage.RowsWithoutIdentity);

            throw new InvalidOperationException(
                $"Orders import received {ordersPage.RowsWithoutIdentity} row(s) without a usable order identity " +
                $"(no gNumber, or no srid and no sticker) in window {request.DateFrom:O} - {request.DateTo:O}; " +
                "the window cannot be imported without inventing an identity that may split or merge real orders.");
        }

        foreach (ChannelOrderSnapshot order in ordersPage.Orders)
        {
            // Sibling windows of one bisection share their boundary instant, so an order placed on
            // that instant is legitimately reported by both halves. That is one order observed
            // twice, not two orders colliding, so the reports are merged - never re-keyed.
            if (!ordersByOrderKey.TryGetValue(order.ExternalOrderId, out ChannelOrderSnapshot? existing))
            {
                ordersByOrderKey.Add(order.ExternalOrderId, order);
                continue;
            }

            ordersByOrderKey[order.ExternalOrderId] = OrderSnapshotMerger.Merge(existing, order);
        }

        // The endpoint filters by dateFrom alone and serves rows in ascending order of the selection
        // instant, capped at MaxOrdersPerResponse rows. A capped response therefore proves that
        // everything up to its last returned instant was served, and says nothing about what lies
        // beyond it. Bisecting the requested window instead re-issues the parent's own request: the
        // left half keeps the parent's dateFrom, the endpoint has no dateTo, so it returns the same
        // block, reports the cap again, and recurses down to the window floor without having read a
        // single new row - failing the run on volume alone. The bisection has to start where the
        // unknown region starts, which is the covered boundary.
        // A null CoveredUntil on a capped response means the source could not prove how far it
        // reached; falling back to the requested DateFrom keeps the run honest, since it narrows
        // and retries rather than assuming the window was served.
        DateTimeOffset nextFrom = request.DateFrom;

        if (ordersPage.CoveredUntil != null && ordersPage.CoveredUntil.Value > nextFrom)
        {
            nextFrom = ordersPage.CoveredUntil.Value;
        }

        // Nothing is left unknown: either the response came back whole, or it reached the window end
        // before hitting the cap, in which case the rows past the window are what filled it.
        if (!ordersPage.HasMore || nextFrom >= request.DateTo)
        {
            return attempt;
        }

        TimeSpan unknownSpan = request.DateTo - nextFrom;

        if (unknownSpan <= _minWindow)
        {
            // Accepting the truncated response would silently drop orders so the operator sees
            // a window granularity that the endpoint cannot serve rather than a partial import
            // reported as success.
            _logger.LogWarning(
                "JobRun {JobRunId}: window {From} - {To} still returns a partial response with {Remaining} left to read at the minimum granularity of {MinWindow}, failing the run",
                jobRunId,
                nextFrom,
                request.DateTo,
                unknownSpan,
                _minWindow);

            throw new InvalidOperationException(
                $"Orders import cannot narrow window {nextFrom:O} - {request.DateTo:O} below {_minWindow} " +
                "while the endpoint keeps reporting a partial response.");
        }

        DateTimeOffset midpoint = nextFrom + TimeSpan.FromTicks(unknownSpan.Ticks / 2);

        OrdersRequest firstHalf = new OrdersRequest
        {
            DateFrom = nextFrom,
            DateTo = midpoint,
            Flag = request.Flag,
            Limit = request.Limit,
        };

        OrdersRequest secondHalf = new OrdersRequest
        {
            DateFrom = midpoint,
            DateTo = request.DateTo,
            Flag = request.Flag,
            Limit = request.Limit,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: window {From} - {To} exceeded the response cap, covered until {CoveredUntil}, splitting at {Midpoint}",
            jobRunId,
            request.DateFrom,
            request.DateTo,
            nextFrom,
            midpoint);

        OrdersAttempt firstHalfAttempt = await FetchWindowAsync(ordersSource, connection, firstHalf, jobRunId, ordersByOrderKey, state, cancellationToken);

        if (firstHalfAttempt.IsStopped)
        {
            return firstHalfAttempt;
        }

        return await FetchWindowAsync(ordersSource, connection, secondHalf, jobRunId, ordersByOrderKey, state, cancellationToken);
    }
}
