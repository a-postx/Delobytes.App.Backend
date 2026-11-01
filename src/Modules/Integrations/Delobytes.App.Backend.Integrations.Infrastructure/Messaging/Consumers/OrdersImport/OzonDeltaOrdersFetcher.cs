using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The Ozon FBO delta branch: reads the window since the last watermark by following the
/// endpoint's own cursor, then publishes the merged result as one unit. Ozon's
/// <c>posting/fbo/list</c> filters on <c>created_at</c> only, so the watermark this branch reports
/// is the newest posting the window returned, not a last-change instant.
/// </summary>
public sealed class OzonDeltaOrdersFetcher : IOrdersFetcher
{
    private readonly IOrdersWindowReader _windowReader;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly OrdersBatchPublisher _batchPublisher;
    private readonly ILogger<OzonDeltaOrdersFetcher> _logger;
    private readonly TimeSpan _safetyOverlap;
    private readonly TimeSpan _backfillWindow;
    private readonly int _pageSize;

    public OzonDeltaOrdersFetcher(
        IOrdersWindowReader windowReader,
        ISyncCheckpointRepository syncCheckpointRepository,
        OrdersBatchPublisher batchPublisher,
        ILogger<OzonDeltaOrdersFetcher> logger,
        IOptions<OzonImportOptions> options)
    {
        OzonImportOptions importOptions = options.Value;

        _windowReader = windowReader;
        _syncCheckpointRepository = syncCheckpointRepository;
        _batchPublisher = batchPublisher;
        _logger = logger;

        _safetyOverlap = TimeSpan.FromMinutes(importOptions.SafetyOverlapMinutes >= 0
            ? importOptions.SafetyOverlapMinutes
            : OzonImportOptions.DefaultSafetyOverlapMinutes);

        _backfillWindow = TimeSpan.FromDays(importOptions.BackfillWindowDays > 0
            ? importOptions.BackfillWindowDays
            : OzonImportOptions.DefaultBackfillWindowDays);

        _pageSize = importOptions.PageSize > 0 ? importOptions.PageSize : OzonImportOptions.DefaultPageSize;
    }

    /// <inheritdoc/>
    public async Task<FetchOutcome> FetchAsync(
        OrdersImportContext context, OrdersRunProgress progress, CancellationToken cancellationToken)
    {
        SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository
            .GetAsync(context.Connection.Id, context.JobType, cancellationToken);

        DateTimeOffset windowFrom = ResolveWindowFrom(syncCheckpoint, context.BackfillState);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = windowFrom,
            DateTo = context.WindowTo,
            Selection = OrdersSelectionMode.LastChanged,
            Schema = context.Schema ?? OrdersSchema.Fbo,
            Limit = _pageSize,
            Cursor = null,
            Statuses = OzonOrdersImportConstants.AllFboStatuses,
        };

        _logger.LogInformation(
            "JobRun {JobRunId}: branch=delta, fetching Ozon FBO orders from {From} to {To}",
            context.JobRun.Id,
            request.DateFrom,
            request.DateTo);

        FetchOutcome outcome = await _windowReader.ReadWindowAsync(request, context, progress, cancellationToken);

        if (outcome.IsStopped)
        {
            // Nothing already read is published: it belongs to an unread window, so publishing it
            // would advance the watermark over postings the cursor walk never saw.
            return outcome;
        }

        DateTimeOffset? maxChangedAt = OrdersWindowPlanner.GetMaxCreatedAt(
            outcome.Orders, context.WindowTo, out DateTimeOffset? observedBeyondWindow);

        if (observedBeyondWindow.HasValue)
        {
            _logger.LogWarning(
                "Ozon orders import: observed created_at {Observed} is later than the run window end {WindowTo}, clamping the watermark",
                observedBeyondWindow.Value,
                context.WindowTo);
        }

        await _batchPublisher.PublishUnitAsync(context, progress, outcome.Orders, cancellationToken);

        return outcome with
        {
            MaxChangedAt = maxChangedAt,
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
