using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Persists the sync checkpoint and the connection's last sync time for a successful orders import
/// run. A run that did not succeed writes nothing: its watermark was never earned, and the next
/// scheduled run must start from the previous one.
/// </summary>
public sealed class OrdersImportWatermarkWriter
{
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrdersImportWatermarkWriter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersImportWatermarkWriter"/> class.
    /// </summary>
    /// <param name="syncCheckpointRepository">Sync checkpoint repository.</param>
    /// <param name="connectionRepository">Connection repository.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public OrdersImportWatermarkWriter(
        ISyncCheckpointRepository syncCheckpointRepository,
        IConnectionRepository connectionRepository,
        TimeProvider timeProvider,
        ILogger<OrdersImportWatermarkWriter> logger)
    {
        _syncCheckpointRepository = syncCheckpointRepository;
        _connectionRepository = connectionRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Writes the watermark carried by <paramref name="payload"/> for a run that succeeded. No-op
    /// for any other status.
    /// </summary>
    /// <param name="jobRun">Finalised run.</param>
    /// <param name="payload">Checkpoint payload the run finished with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WriteAsync(
        JobRun jobRun, OrdersImportCheckpointPayload payload, CancellationToken cancellationToken)
    {
        if (jobRun.Status != JobStatus.Success)
        {
            return;
        }

        DateTimeOffset? watermark = ResolveWatermark(payload);

        SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository.GetAsync(
            jobRun.ConnectionId, jobRun.JobType, cancellationToken);

        if (syncCheckpoint == null)
        {
            // A null watermark on a brand-new checkpoint is correct for a mid-walk backfill run:
            // the handover writes nothing until the walk finishes, and the delta branch already
            // treats an absent watermark as "walk not handed over yet" rather than as "start
            // syncing from here".
            syncCheckpoint = new SyncCheckpoint
            {
                Id = Guid.NewGuid(),
                ConnectionId = jobRun.ConnectionId,
                JobType = jobRun.JobType,
                LastChangedAt = watermark,
                LastSuccessAt = _timeProvider.GetUtcNow(),
                LastJobRunId = jobRun.Id
            };
        }
        else
        {
            if (watermark != null)
            {
                syncCheckpoint.LastChangedAt = watermark;
            }

            syncCheckpoint.LastSuccessAt = _timeProvider.GetUtcNow();
            syncCheckpoint.LastJobRunId = jobRun.Id;
        }

        await _syncCheckpointRepository.UpsertAsync(syncCheckpoint, cancellationToken);

        Connection? connection = await _connectionRepository.FindByIdAsync(jobRun.ConnectionId, cancellationToken);

        if (connection != null)
        {
            connection.LastSyncAt = _timeProvider.GetUtcNow();
            await _connectionRepository.SaveChangesAsync(cancellationToken);
        }

        _logger.LogInformation(
            "Updated SyncCheckpoint for ConnectionId={ConnectionId}, JobType={JobType}, IsBackfill={IsBackfill}, LastChangedAt={LastChangedAt}, WatermarkWritten={WatermarkWritten}",
            jobRun.ConnectionId,
            jobRun.JobType,
            payload.IsBackfill,
            watermark,
            watermark != null);
    }

    private DateTimeOffset? ResolveWatermark(OrdersImportCheckpointPayload payload)
    {
        if (payload.Phase != OrdersImportCheckpointPhase.FetchCompleted)
        {
            if (payload.IsBackfill)
            {
                _logger.LogInformation(
                    "Backfill run has not finished catch-up, leaving the delta watermark untouched");
            }

            return null;
        }

        if (!payload.IsBackfill)
        {
            // The watermark is the newest source change date the orchestrator actually observed,
            // not the wall clock: "now" would claim changes arriving between the fetch and this
            // finalisation as already imported, and the next delta run would skip them. A run that
            // fetched nothing leaves the previous watermark intact.
            return payload.MaxChangedAt;
        }

        if (!payload.IsBackfillCompleted)
        {
            _logger.LogInformation(
                "Backfill run has not finished catch-up, leaving the delta watermark untouched");

            return null;
        }

        return payload.HandoverWatermark
            ?? throw new InvalidOperationException("Completed orders backfill has no handover watermark.");
    }
}
