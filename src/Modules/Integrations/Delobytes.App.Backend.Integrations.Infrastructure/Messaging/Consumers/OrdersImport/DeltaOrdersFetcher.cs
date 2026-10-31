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

    public async Task<FetchOutcome> FetchAsync(OrdersImportContext context, OrdersRunProgress progress, CancellationToken cancellationToken)
    {
        SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository
            .GetAsync(context.Connection.Id, JobType.OrdersImport, cancellationToken);

        DateTimeOffset windowFrom = OrdersWindowPlanner.ResolveDeltaWindowFrom(
            syncCheckpoint, context.BackfillState, out bool usedLegacyFallback);

        if (usedLegacyFallback)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: legacy completed backfill has no handover watermark; using walk start {WindowFrom}",
                context.JobRun.Id, windowFrom);
        }

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = windowFrom.ToOffset(OrdersWindowPlanner.MoscowOffset),
            DateTo = context.WindowTo.ToOffset(OrdersWindowPlanner.MoscowOffset),
            Selection = OrdersSelectionMode.LastChanged,
            Limit = 0,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=delta, fetching orders from {From} to {To}",
            context.JobRun.Id,
            request.DateFrom,
            request.DateTo);

        FetchOutcome outcome = await _bisectingReader.FetchAllPagesAsync(
            request, context, progress, cancellationToken);

        if (outcome.IsStopped)
        {
            // Nothing already read is published: it belongs to an unread window, so publishing it
            // would advance the watermark over orders the bisection never saw.
            return outcome;
        }

        DateTimeOffset? maxChangedAt = OrdersWindowPlanner.GetMaxChangedAt(
            outcome.Orders, context.WindowTo, out DateTimeOffset? observedBeyondWindow);

        if (observedBeyondWindow.HasValue)
        {
            _logger.LogWarning(
                "Orders import: observed lastChangeDate {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                observedBeyondWindow.Value,
                context.WindowTo);
        }

        await _batchPublisher.PublishUnitAsync(context, progress, outcome.Orders, cancellationToken);

        return outcome with
        {
            MaxChangedAt = maxChangedAt,
        };
    }
}
