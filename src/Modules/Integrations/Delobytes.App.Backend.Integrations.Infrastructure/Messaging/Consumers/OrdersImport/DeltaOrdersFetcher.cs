using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The delta branch: reads the whole window since the last watermark, bisecting it while the
/// endpoint reports a partial response, then publishes the merged result as one unit.
/// </summary>
public sealed class DeltaOrdersFetcher : IOrdersFetcher
{
    private readonly BisectingOrdersReader _bisectingReader;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly OrdersBatchPublisher _batchPublisher;
    private readonly ILogger<DeltaOrdersFetcher> _logger;

    public DeltaOrdersFetcher(
        BisectingOrdersReader bisectingReader,
        ISyncCheckpointRepository syncCheckpointRepository,
        OrdersBatchPublisher batchPublisher,
        ILogger<DeltaOrdersFetcher> logger)
    {
        _bisectingReader = bisectingReader;
        _syncCheckpointRepository = syncCheckpointRepository;
        _batchPublisher = batchPublisher;
        _logger = logger;
    }

    public async Task<FetchOutcome> FetchAsync(OrdersImportRun run, CancellationToken cancellationToken)
    {
        IOrdersSource ordersSource = run.OrdersSource
            ?? throw new InvalidOperationException("Delta fetch requires a resolved orders source.");
        OrdersBackfillState backfillState = run.BackfillState
            ?? throw new InvalidOperationException("Delta fetch requires the connection's backfill cursor.");

        SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository
            .GetAsync(run.Connection.Id, JobType.OrdersImport, cancellationToken);

        DateTimeOffset windowFrom = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            syncCheckpoint, backfillState, out bool usedLegacyFallback);

        if (usedLegacyFallback)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: legacy completed backfill has no handover watermark; using walk start {WindowFrom}",
                run.JobRun.Id, windowFrom);
        }

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = windowFrom.ToOffset(OrdersWindowPlanner.MoscowOffset),
            DateTo = run.WindowTo.ToOffset(OrdersWindowPlanner.MoscowOffset),
            Flag = 0,
            Limit = 0,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=delta, fetching orders from {From} to {To}",
            run.JobRun.Id,
            request.DateFrom,
            request.DateTo);

        FetchOutcome outcome = await _bisectingReader.FetchAllPagesAsync(
            ordersSource, request, run, cancellationToken);

        if (outcome.IsStopped)
        {
            // Nothing already read is published: it belongs to an unread window, so publishing it
            // would advance the watermark over orders the bisection never saw.
            return outcome;
        }

        DateTimeOffset? maxChangedAt = OrdersWindowPlanner.GetMaxChangedAt(
            outcome.Orders, run.WindowTo, out DateTimeOffset? observedBeyondWindow);

        if (observedBeyondWindow.HasValue)
        {
            _logger.LogWarning(
                "Orders import: observed lastChangeDate {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                observedBeyondWindow.Value,
                run.WindowTo);
        }

        await _batchPublisher.PublishUnitAsync(run, outcome.Orders, cancellationToken);

        return outcome with
        {
            MaxChangedAt = maxChangedAt,
        };
    }
}
