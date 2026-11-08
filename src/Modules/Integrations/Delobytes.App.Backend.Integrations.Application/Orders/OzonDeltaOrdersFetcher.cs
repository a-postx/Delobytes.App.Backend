using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// The Ozon delta branch, per schema: reads the window since the last watermark by following the
/// endpoint's own cursor, then publishes the merged result as one unit. Ozon's posting list
/// endpoints filter on <c>created_at</c> only, so the watermark this branch reports is the newest
/// posting the window returned, not a last-change instant. The schema is a constructor argument
/// because the statuses differ between FBO and FBS and must match the endpoint being called.
/// </summary>
public sealed class OzonDeltaOrdersFetcher : IOrdersFetcher
{
    private readonly IOrdersWindowReader _windowReader;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly OrdersBatchPublisher _batchPublisher;
    private readonly ILogger<OzonDeltaOrdersFetcher> _logger;
    private readonly OrdersSchema _schema;
    private readonly IReadOnlyList<string> _statuses;
    private readonly TimeSpan _safetyOverlap;
    private readonly TimeSpan _backfillWindow;
    private readonly int _pageSize;

    public OzonDeltaOrdersFetcher(
        IOrdersWindowReader windowReader,
        ISyncCheckpointRepository syncCheckpointRepository,
        OrdersBatchPublisher batchPublisher,
        ILogger<OzonDeltaOrdersFetcher> logger,
        OrdersImportTuning tuning,
        OrdersSchema schema,
        IReadOnlyList<string> statuses)
    {
        _windowReader = windowReader;
        _syncCheckpointRepository = syncCheckpointRepository;
        _batchPublisher = batchPublisher;
        _logger = logger;
        _schema = schema;
        _statuses = statuses;

        _safetyOverlap = tuning.SafetyOverlap;
        _backfillWindow = tuning.BackfillWindow;
        _pageSize = tuning.PageSize ?? OzonImportOptions.DefaultPageSize;
    }

    /// <inheritdoc/>
    public async Task<FetchOutcome> FetchAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        CancellationToken cancellationToken)
    {
        SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository
            .GetAsync(context.Connection.Id, context.JobType, cancellationToken);

        DateTimeOffset windowFrom = ResolveWindowFrom(syncCheckpoint, context.BackfillState);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = windowFrom,
            DateTo = context.WindowTo,
            Selection = OrdersSelectionMode.LastChanged,
            Schema = _schema,
            Limit = _pageSize,
            Cursor = null,
            Statuses = _statuses,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=delta, fetching Ozon {Schema} orders from {From} to {To}",
            context.JobRun.Id,
            _schema,
            request.DateFrom,
            request.DateTo);

        FetchOutcome outcome = await _windowReader.ReadWindowAsync(request, context, counters, cancellationToken);

        if (outcome is FetchOutcome.Stopped)
        {
            // Nothing already read is published: it belongs to an unread window, so publishing it
            // would advance the watermark over postings the cursor walk never saw.
            return outcome;
        }

        FetchOutcome.DeltaRead read = (FetchOutcome.DeltaRead)outcome;

        WatermarkResolution watermark = OrdersWindowPlanner.GetMaxCreatedAt(
            read.Orders, context.WindowTo);

        if (watermark.ObservedBeyondWindow.HasValue)
        {
            _logger.LogWarning(
                "Ozon orders import: observed created_at {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                watermark.ObservedBeyondWindow.Value,
                context.WindowTo);
        }

        await _batchPublisher.PublishUnitAsync(context, counters, batches, read.Orders, cancellationToken);

        return read with
        {
            MaxChangedAt = watermark.Value,
        };
    }

    /// <summary>
    /// Prefers the stored watermark, then the one the backfill handed over, and only then the
    /// legacy fallback of the backfill scope start plus its width, so a connection converted from
    /// the old single-request import resumes where the walk left it.
    /// </summary>
    private DateTimeOffset ResolveWindowFrom(SyncCheckpoint? syncCheckpoint, OrdersBackfillState backfillState)
    {
        if (syncCheckpoint?.LastChangedAt != null)
        {
            return syncCheckpoint.LastChangedAt.Value - _safetyOverlap;
        }

        return backfillState.HandoverWatermark ?? backfillState.OldestDayInScope + _backfillWindow;
    }
}
