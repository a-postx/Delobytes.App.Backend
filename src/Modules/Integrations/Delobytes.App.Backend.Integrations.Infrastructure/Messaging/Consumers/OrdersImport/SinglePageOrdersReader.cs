using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Reads one calendar day of a date-window channel that cannot paginate inside the day at all: one
/// request, one response, and a partial response is fatal because there is no smaller unit to ask
/// for. This is the strategy the Wildberries <c>flag=1</c> backfill needs, where a day IS the window
/// and bisecting it would only re-request the same block.
/// </summary>
public sealed class SinglePageOrdersReader : IOrdersWindowReader
{
    private readonly PacedOrdersSource _pacedSource;
    private readonly ILogger<SinglePageOrdersReader> _logger;
    private readonly string _methodName;
    private readonly TimeSpan _maxInProcessWait;

    /// <summary>
    /// Initializes a new instance of the <see cref="SinglePageOrdersReader"/> class.
    /// </summary>
    /// <param name="pacedSource">Issues the page request against the run's budget and pacing.</param>
    /// <param name="logger">Logger for the failure diagnostics.</param>
    /// <param name="methodName">The channel API method being read, see
    /// <see cref="OrdersRateLimiterMethods"/>; it selects the pacing bucket.</param>
    /// <param name="maxInProcessWait">Longest throttle the run may wait out inside the request.</param>
    public SinglePageOrdersReader(
        PacedOrdersSource pacedSource,
        ILogger<SinglePageOrdersReader> logger,
        string methodName,
        TimeSpan maxInProcessWait)
    {
        _pacedSource = pacedSource;
        _logger = logger;
        _methodName = methodName;
        _maxInProcessWait = maxInProcessWait;
    }

    /// <summary>
    /// Reads the window in exactly one request. A stop is returned as a value so the day walk can
    /// reschedule without losing its cursor; a partial or identity-dropping response fails the run,
    /// since neither can be repaired by asking the same endpoint for less.
    /// </summary>
    public async Task<FetchOutcome> ReadWindowAsync(
        OrdersRequest request,
        OrdersImportContext context,
        OrdersRunProgress progress,
        CancellationToken cancellationToken)
    {
        OrdersAttempt attempt = await _pacedSource.GetOrdersAsync(
            context,
            progress,
            request,
            _methodName,
            _maxInProcessWait,
            cancellationToken);

        if (attempt.IsStopped)
        {
            return FetchOutcome.Stopped(
                attempt.StopReason,
                attempt.ResumeAt,
                progress.RequestsIssued);
        }

        OrdersPage page = attempt.Page!;

        if (page.RowsWithoutIdentity > 0)
        {
            // A day that dropped rows cannot be imported partially: the missing orders would be
            // indistinguishable from orders that never existed, and walking the cursor past this
            // day would hide them from every later delta run as well.
            _logger.LogError(
                "JobRun {JobRunId}: window {From} - {To} returned {RowsWithoutIdentity} row(s) without a usable order identity, failing the run",
                context.JobRun.Id,
                request.DateFrom,
                request.DateTo,
                page.RowsWithoutIdentity);

            throw new InvalidOperationException(
                $"Orders backfill received {page.RowsWithoutIdentity} row(s) without a usable order identity " +
                $"for day {request.DateFrom:yyyy-MM-dd} MSK.");
        }

        if (page.HasMore)
        {
            // A day is the floor this endpoint can be asked for, so a partial day cannot be narrowed
            // and re-requested. Importing what came back would silently drop orders, which is exactly
            // the defect the backfill walk exists to avoid.
            _logger.LogWarning(
                "JobRun {JobRunId}: window {From} - {To} still returns a partial response at the day granularity, failing the run",
                context.JobRun.Id,
                request.DateFrom,
                request.DateTo);

            throw new InvalidOperationException(
                $"Orders backfill received a partial response for day {request.DateFrom:yyyy-MM-dd} MSK; " +
                "flag=1 cannot be split below one day.");
        }

        List<ChannelOrderSnapshot> orders = OrderSnapshotMerger.MergeByExternalOrderId(page.Orders);

        return new FetchOutcome
        {
            Orders = orders,
            OrdersFetched = orders.Count,
            RequestsIssued = progress.RequestsIssued,
            StopReason = StopReason.Completed,
            ResumeAt = null,
        };
    }
}
