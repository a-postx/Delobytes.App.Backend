using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Aggregates orders batch results and finalises the JobRun only once every batch announced by
/// the orchestrator has been recorded, using the expected batch count the orchestrator announces
/// via <see cref="OrdersImportFetchCompletedEvent"/>. Finalising on the first batch result to
/// arrive would mark a multi-batch run terminal while later batches were still in flight, freezing
/// its counters at a fraction of the real totals and letting the next scheduled run start against
/// a watermark this one never earned.
/// Two independent entry points can trigger finalisation, because the completion event and the
/// batch results it describes can be delivered in either order: a batch result checks the
/// <see cref="JobRun.ExpectedBatchCount"/> already recorded on the JobRun, and the completion
/// event sets that same field and re-checks. Idempotent: duplicate batch deliveries are skipped
/// via the batch's deterministic BatchId, and concurrent finalisation attempts from either entry
/// point are serialised via JobRun's xmin concurrency token.
/// </summary>
public class ProcessOrdersBatchImportCompletedConsumer
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProcessOrdersBatchImportCompletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessOrdersBatchImportCompletedConsumer"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="jobCheckpointRepository">Job checkpoint repository, holds the fetch watermark.</param>
    /// <param name="syncCheckpointRepository">Sync checkpoint repository.</param>
    /// <param name="connectionRepository">Connection repository.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    public ProcessOrdersBatchImportCompletedConsumer(
        IJobRunRepository jobRunRepository,
        IJobCheckpointRepository jobCheckpointRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IConnectionRepository connectionRepository,
        IEventPublisher eventPublisher,
        ILogger<ProcessOrdersBatchImportCompletedConsumer> logger)
    {
        _jobRunRepository = jobRunRepository;
        _jobCheckpointRepository = jobCheckpointRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _connectionRepository = connectionRepository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Processes the OrdersBatchImportCompletedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="batchId">
    /// The batch's deterministic identifier, propagated unchanged from
    /// <see cref="OrdersBatchImportRequestedEvent.BatchId"/>. Used as the idempotency key: the
    /// orchestrator regenerates the same BatchId for a given (JobRunId, batch index), so a
    /// redelivered orchestration republishes batches whose results are recognised as already
    /// recorded instead of being counted twice. A per-publish transport MessageId cannot serve
    /// this purpose, because every republication mints a fresh one.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(
        OrdersBatchImportCompletedEvent message,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessOrdersBatchImportCompletedConsumer: JobRunId={JobRunId}, BatchId={BatchId}",
            message.JobRunId,
            batchId);

        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} not found for batch result BatchId={BatchId}, skipping",
                message.JobRunId,
                batchId);
            return;
        }

        bool alreadyProcessed = await _jobRunRepository.BatchResultExistsAsync(batchId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Batch result BatchId={BatchId} for JobRun {JobRunId} already recorded, skipping",
                batchId,
                message.JobRunId);

            // A redelivered batch result that arrives after the job was finalised must still nudge
            // downstream consumers, which may have missed the original OrdersImportCompletedEvent.
            if (IsTerminalStatus(jobRun.Status))
            {
                await PublishCompletedEventAsync(jobRun, cancellationToken);
            }

            return;
        }

        JobRunBatchResult batchResult = new JobRunBatchResult
        {
            Id = Guid.NewGuid(),
            JobRunId = message.JobRunId,
            MessageId = batchId,
            RecordsProcessed = message.Created + message.Updated + message.Skipped + message.Failed,
            RecordsCreated = message.Created,
            RecordsUpdated = message.Updated,
            RecordsSkipped = message.Skipped,
            RecordsFailed = message.Failed,
            ErrorMessage = null,
            IsLastBatch = false,
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepository.AddBatchResult(batchResult);

        await _jobRunRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded batch result for JobRun {JobRunId}, BatchId={BatchId}",
            message.JobRunId,
            batchId);

        await TryFinalizeAsync(jobRun, cancellationToken);
    }

    /// <summary>
    /// Processes the OrdersImportFetchCompletedEvent message, published by the orchestrator once
    /// it has finished fetching a run and persisted the final checkpoint.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessFetchCompletedAsync(
        OrdersImportFetchCompletedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessOrdersBatchImportCompletedConsumer: fetch completed for JobRunId={JobRunId}, TotalBatches={TotalBatches}",
            message.JobRunId,
            message.TotalBatches);

        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} not found for fetch-completed event, skipping",
                message.JobRunId);
            return;
        }

        // A redelivered completion event for an already-finalised run must still nudge downstream
        // consumers, which may have missed the original OrdersImportCompletedEvent.
        if (IsTerminalStatus(jobRun.Status))
        {
            await PublishCompletedEventAsync(jobRun, cancellationToken);
            return;
        }

        if (jobRun.ExpectedBatchCount != message.TotalBatches)
        {
            jobRun.ExpectedBatchCount = message.TotalBatches;

            try
            {
                await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
            }
            catch (DbUpdateConcurrencyException)
            {
                // Another writer (a concurrently processed batch result that already finalised the
                // run, or a redelivered copy of this same event) updated the JobRun first. Reload
                // its current state instead of overwriting whatever it wrote.
                JobRun? reloaded = await _jobRunRepository.GetByIdAsync(message.JobRunId, cancellationToken);

                if (reloaded == null)
                {
                    return;
                }

                if (IsTerminalStatus(reloaded.Status))
                {
                    await PublishCompletedEventAsync(reloaded, cancellationToken);
                    return;
                }

                jobRun = reloaded;
            }
        }

        await TryFinalizeAsync(jobRun, cancellationToken);
    }

    /// <summary>
    /// Finalises the JobRun once <see cref="JobRun.ExpectedBatchCount"/> is known and every
    /// expected batch result has been recorded. Called from both entry points, because either one
    /// can be the delivery that brings the recorded count up to the expected total.
    /// </summary>
    private async Task TryFinalizeAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        // Re-read rather than trusting the instance loaded by the caller: the completion event can
        // be consumed between that load and this check, so the tracked copy may not carry the
        // threshold yet. Without the re-read the run would never reach finalisation, because no
        // further delivery is guaranteed to arrive.
        int? expectedBatchCount = await _jobRunRepository.GetExpectedBatchCountAsync(jobRun.Id, cancellationToken);

        if (expectedBatchCount is null)
        {
            // The expected batch count is unknown, so there is no way to tell whether every batch
            // of the run has arrived. Leaving the JobRun untouched keeps it eligible for the
            // watchdog (Running beyond the job type's timeout is failed), rather than declaring a
            // partial import complete.
            _logger.LogWarning(
                "JobRun {JobRunId}: expected batch count is unavailable (fetch-completed event not yet received), not finalising",
                jobRun.Id);

            return;
        }

        int recordedBatches = await _jobRunRepository.CountBatchResultsAsync(jobRun.Id, cancellationToken);

        if (recordedBatches < expectedBatchCount.Value)
        {
            // Other batches are still being processed. Whichever result brings the count up to
            // ExpectedBatchCount finalises the job — it is not necessarily the last one the
            // orchestrator published, since a small trailing batch can finish before its larger
            // siblings. Counters are deliberately not written yet: they are an aggregate over
            // every batch, so a mid-run write would publish a partial total as if it were final.
            _logger.LogInformation(
                "JobRun {JobRunId}: {Recorded}/{Expected} batch results recorded, waiting for the rest",
                jobRun.Id,
                recordedBatches,
                expectedBatchCount);

            return;
        }

        // The instance being finalised may predate the completion event, so carry the re-read
        // threshold onto it; the UPDATE below persists it together with the final counters.
        jobRun.ExpectedBatchCount = expectedBatchCount;

        JobCheckpoint? checkpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRun.Id, cancellationToken);
        OrdersImportCheckpointPayload? payload = DeserializePayload(jobRun.Id, checkpoint?.Payload);

        if (payload == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: checkpoint payload is unavailable (missing or unreadable), not finalising",
                jobRun.Id);

            return;
        }

        JobRunBatchResultTotals totals = await _jobRunRepository.GetBatchResultTotalsAsync(
            jobRun.Id, cancellationToken);

        jobRun.RecordsCreated = totals.TotalCreated;
        jobRun.RecordsUpdated = totals.TotalUpdated;
        jobRun.RecordsSkipped = totals.TotalSkipped;
        jobRun.RecordsFailed = totals.TotalFailed;
        jobRun.CompletedAt = DateTimeOffset.UtcNow;

        bool hasErrors = totals.TotalFailed > 0 || totals.CombinedErrors != null;

        if (jobRun.Status == JobStatus.Cancelled)
        {
            // Preserve the cancellation outcome — the counters above already reflect whatever was
            // actually processed before the cooperative stop took effect.
            jobRun.Error = totals.CombinedErrors;
        }
        else if (!hasErrors)
        {
            jobRun.Status = JobStatus.Success;
        }
        else
        {
            // JobStatus has no "partially succeeded" value: any failed record makes the run Failed
            // even if other records in the same batch succeeded.
            jobRun.Status = JobStatus.Failed;
            jobRun.Error = totals.CombinedErrors;
            _logger.LogError("Error importing orders batch: {Message}", totals.CombinedErrors);
        }

        try
        {
            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // JobRun.RowVersion (xmin) is the concurrency token: the UPDATE above carried the row
            // version loaded at the start of this call. Zero rows were affected because the other
            // entry point — processed concurrently against the same recordedBatches threshold —
            // already finalised the job first and published OrdersImportCompletedEvent. Stepping
            // aside here avoids a double finalisation.
            _logger.LogInformation(
                "JobRun {JobRunId} was already finalised concurrently, skipping",
                jobRun.Id);

            return;
        }

        _logger.LogInformation(
            "JobRun {JobRunId} finalised with status {Status}, total Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            jobRun.Id,
            jobRun.Status,
            jobRun.RecordsCreated,
            jobRun.RecordsUpdated,
            jobRun.RecordsSkipped,
            jobRun.RecordsFailed);

        if (jobRun.Status == JobStatus.Success)
        {
            DateTimeOffset? watermark = ResolveWatermark(payload);

            SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository.GetAsync(
                jobRun.ConnectionId, JobType.OrdersImport, cancellationToken);

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
                    JobType = JobType.OrdersImport,
                    LastChangedAt = watermark,
                    LastSuccessAt = DateTimeOffset.UtcNow,
                    LastJobRunId = jobRun.Id
                };
            }
            else
            {
                if (watermark != null)
                {
                    syncCheckpoint.LastChangedAt = watermark;
                }

                syncCheckpoint.LastSuccessAt = DateTimeOffset.UtcNow;
                syncCheckpoint.LastJobRunId = jobRun.Id;
            }

            await _syncCheckpointRepository.UpsertAsync(syncCheckpoint, cancellationToken);

            Connection? connection = await _connectionRepository.FindByIdAsync(jobRun.ConnectionId, cancellationToken);

            if (connection != null)
            {
                connection.LastSyncAt = DateTimeOffset.UtcNow;
                await _connectionRepository.SaveChangesAsync(cancellationToken);
            }

            _logger.LogInformation(
                "Updated SyncCheckpoint for ConnectionId={ConnectionId}, JobType={JobType}, IsBackfill={IsBackfill}, LastChangedAt={LastChangedAt}, WatermarkWritten={WatermarkWritten}",
                jobRun.ConnectionId,
                JobType.OrdersImport,
                payload.IsBackfill,
                watermark,
                watermark != null);
        }

        await PublishCompletedEventAsync(jobRun, cancellationToken);
    }

    private DateTimeOffset? ResolveWatermark(OrdersImportCheckpointPayload payload)
    {
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

    private async Task PublishCompletedEventAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        OrdersImportCompletedEvent completedEvent = new OrdersImportCompletedEvent
        {
            JobRunId = jobRun.Id,
            TotalCreated = jobRun.RecordsCreated,
            TotalUpdated = jobRun.RecordsUpdated,
            Status = jobRun.Status
        };

        await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

        _logger.LogInformation(
            "Published OrdersImportCompletedEvent for JobRun {JobRunId}",
            jobRun.Id);
    }

    private static bool IsTerminalStatus(JobStatus status)
    {
        return status == JobStatus.Success
            || status == JobStatus.Failed
            || status == JobStatus.Cancelled;
    }

    /// <summary>
    /// Same decision as the orchestrator's checkpoint store, except that an unreadable payload is
    /// reported as "unknown" here instead of thrown: the aggregator's documented behaviour for an
    /// unknown watermark source is to leave the JobRun for the watchdog, and failing one batch
    /// result would not repair the checkpoint anyway.
    /// </summary>
    private static OrdersImportCheckpointPayload? DeserializePayload(Guid jobRunId, string? payload)
    {
        try
        {
            return OrdersImportCheckpointStore.DeserializePayload(jobRunId, payload);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
