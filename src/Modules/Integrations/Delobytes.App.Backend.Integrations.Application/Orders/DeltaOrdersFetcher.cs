using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

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

    public async Task<FetchOutcome> FetchAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        CancellationToken cancellationToken)
    {
        SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository
            .GetAsync(context.Connection.Id, context.JobType, cancellationToken);

        DeltaWindow window = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            syncCheckpoint, context.BackfillState);

        if (window.UsedLegacyFallback)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: legacy completed backfill has no handover watermark; using walk start {WindowFrom}",
                context.JobRun.Id, window.From);
        }

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = window.From.ToOffset(OrdersWindowPlanner.MoscowTime),
            DateTo = context.WindowTo.ToOffset(OrdersWindowPlanner.MoscowTime),
            Selection = OrdersSelectionMode.LastChanged,
            Limit = 0,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=delta, fetching orders from {From} to {To}",
            context.JobRun.Id,
            request.DateFrom,
            request.DateTo);

        FetchOutcome outcome = await _bisectingReader.ReadWindowAsync(
            request, context, counters, cancellationToken);

        if (outcome is FetchOutcome.Stopped)
        {
            // Nothing already read is published: it belongs to an unread window, so publishing it
            // would advance the watermark over orders the bisection never saw.
            return outcome;
        }

        FetchOutcome.DeltaRead read = (FetchOutcome.DeltaRead)outcome;

        WatermarkResolution watermark = OrdersWindowPlanner.GetMaxChangedAt(
            read.Orders, context.WindowTo);

        if (watermark.ObservedBeyondWindow.HasValue)
        {
            _logger.LogWarning(
                "Orders import: observed lastChangeDate {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                watermark.ObservedBeyondWindow.Value,
                context.WindowTo);
        }

        await _batchPublisher.PublishUnitAsync(context, counters, batches, read.Orders, cancellationToken);

        return read with
        {
            MaxChangedAt = watermark.Value,
        };
    }
}
