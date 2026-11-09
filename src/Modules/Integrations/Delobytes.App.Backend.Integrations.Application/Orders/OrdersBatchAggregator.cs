using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Aggregates the deliveries that report on one orders import run: it records batch results, learns
/// the batch count the orchestrator announced, and finalises the run only once every announced batch
/// has been recorded. Finalising on the first batch result to arrive would mark a multi-batch run
/// terminal while later batches were still in flight, freezing its counters at a fraction of the real
/// totals and letting the next scheduled run start against a watermark this one never earned.
/// The threshold is read from <see cref="JobRun.ExpectedBatchCount"/> and, when the completion event
/// has not written it yet, from the run's own checkpoint: the checkpoint is the durable record the
/// orchestrator writes before it announces anything, so a batch result delivered ahead of the event
/// reads the same number the event carries. That ordering is the normal one — batches are published
/// mid-fetch — so it is not treated as an anomaly.
/// </summary>
public sealed class OrdersBatchAggregator
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly BatchImportFinalizer _finalizer;
    private readonly OrdersImportWatermarkWriter _watermarkWriter;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrdersBatchAggregator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersBatchAggregator"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="jobCheckpointRepository">Job checkpoint repository, holds the fetch watermark.</param>
    /// <param name="finalizer">Finaliser that writes the run's final counters and status.</param>
    /// <param name="watermarkWriter">Writer that persists the synced position of a successful run.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public OrdersBatchAggregator(
        IJobRunRepository jobRunRepository,
        IJobCheckpointRepository jobCheckpointRepository,
        BatchImportFinalizer finalizer,
        OrdersImportWatermarkWriter watermarkWriter,
        TimeProvider timeProvider,
        ILogger<OrdersBatchAggregator> logger)
    {
        _jobRunRepository = jobRunRepository;
        _jobCheckpointRepository = jobCheckpointRepository;
        _finalizer = finalizer;
        _watermarkWriter = watermarkWriter;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Records one batch result under the batch's deterministic id, then finalises if it was the last
    /// outstanding one.
    /// </summary>
    /// <param name="jobRunId">Run the batch belongs to.</param>
    /// <param name="message">Batch result as delivered by the transport.</param>
    /// <param name="batchId">Deterministic batch identifier used as the idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run and whether its completion event is owed, or <c>null</c> when the run is gone.</returns>
    public async Task<OrdersImportCompletion?> RecordBatchResultAsync(
        Guid jobRunId,
        OrdersBatchImportCompletedEvent message,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        JobRun? jobRun = await GetJobRunAsync(jobRunId, "batch result", cancellationToken);

        if (jobRun == null)
        {
            return null;
        }

        if (await _jobRunRepository.BatchResultExistsAsync(batchId, cancellationToken))
        {
            _logger.LogInformation(
                "Batch result BatchId={BatchId} for JobRun {JobRunId} already recorded, skipping",
                batchId,
                jobRunId);

            return new OrdersImportCompletion(jobRun, IsTerminalStatus(jobRun.Status));
        }

        _jobRunRepository.AddBatchResult(new JobRunBatchResult
        {
            Id = Guid.NewGuid(),
            JobRunId = jobRunId,
            MessageId = batchId,
            RecordsProcessed = message.Created + message.Updated + message.Skipped + message.Failed,
            RecordsCreated = message.Created,
            RecordsUpdated = message.Updated,
            RecordsSkipped = message.Skipped,
            RecordsFailed = message.Failed,
            ErrorMessage = null,
            IsLastBatch = false,
            ReceivedAt = _timeProvider.GetUtcNow(),
        });

        await _jobRunRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded batch result for JobRun {JobRunId}, BatchId={BatchId}",
            jobRunId,
            batchId);

        BatchImportFinalizationResult result = await TryFinalizeAsync(jobRun, cancellationToken);

        return new OrdersImportCompletion(jobRun, result == BatchImportFinalizationResult.Finalized);
    }

    /// <summary>
    /// Records the batch count announced by the completion event, then finalises if that was the only
    /// missing piece.
    /// </summary>
    /// <param name="jobRunId">Run the event belongs to.</param>
    /// <param name="totalBatches">Batch count announced by the completion event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The run and whether its completion event is owed, or <c>null</c> when the run is gone. The
    /// event is owed when this call finalised the run, and also when the run turned out to be
    /// terminal already: a redelivered event must still nudge downstream consumers.
    /// </returns>
    public async Task<OrdersImportCompletion?> RecordExpectedBatchCountAsync(
        Guid jobRunId, int totalBatches, CancellationToken cancellationToken)
    {
        JobRun? jobRun = await GetJobRunAsync(jobRunId, "fetch-completed event", cancellationToken);

        if (jobRun == null)
        {
            return null;
        }

        if (IsTerminalStatus(jobRun.Status))
        {
            return new OrdersImportCompletion(jobRun, true);
        }

        if (jobRun.ExpectedBatchCount != totalBatches)
        {
            jobRun.ExpectedBatchCount = totalBatches;

            bool updated = await _jobRunRepository.TryUpdateAsync(jobRun, cancellationToken);

            if (!updated)
            {
                // Another writer (a concurrently processed batch result that already finalised the
                // run, or a redelivered copy of this same event) updated the JobRun first. Reload
                // its current state instead of overwriting whatever it wrote.
                JobRun? reloaded = await _jobRunRepository.GetByIdAsync(jobRunId, cancellationToken);

                if (reloaded == null)
                {
                    return new OrdersImportCompletion(jobRun, false);
                }

                if (IsTerminalStatus(reloaded.Status))
                {
                    return new OrdersImportCompletion(reloaded, true);
                }

                jobRun = reloaded;
            }
        }

        BatchImportFinalizationResult result = await TryFinalizeAsync(jobRun, cancellationToken);

        return new OrdersImportCompletion(jobRun, result == BatchImportFinalizationResult.Finalized);
    }

    /// <summary>
    /// Finalises the run once the expected batch count is known and every expected batch result has
    /// been recorded. Reached from both kinds of delivery, because either one can be the delivery that
    /// brings the recorded count up to the expected total.
    /// </summary>
    private async Task<BatchImportFinalizationResult> TryFinalizeAsync(
        JobRun jobRun, CancellationToken cancellationToken)
    {
        // Re-read rather than trusting the instance loaded by the caller: the completion event can
        // be consumed between that load and this check, so the tracked copy may not carry the
        // threshold yet. Without the re-read the run would never reach finalisation, because no
        // further delivery is guaranteed to arrive.
        int? expectedBatchCount = await _jobRunRepository.GetExpectedBatchCountAsync(jobRun.Id, cancellationToken);
        OrdersImportCheckpointPayload? checkpointPayload = null;

        if (expectedBatchCount is null)
        {
            CheckpointThreshold threshold = await ResolveCheckpointThresholdAsync(jobRun, cancellationToken);

            if (!threshold.IsKnown)
            {
                return threshold.Outcome;
            }

            expectedBatchCount = threshold.ExpectedBatchCount;
            checkpointPayload = threshold.Payload;
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

            return BatchImportFinalizationResult.NotReady;
        }

        // The instance being finalised may predate the completion event, so carry the re-read
        // threshold onto it; the UPDATE below persists it together with the final counters.
        jobRun.ExpectedBatchCount = expectedBatchCount;

        OrdersImportCheckpointPayload? payload = checkpointPayload;

        if (payload == null)
        {
            JobCheckpoint? checkpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRun.Id, cancellationToken);
            payload = OrdersImportCheckpointStore.TryDeserializePayload(jobRun.Id, checkpoint?.Payload);
        }

        if (payload == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: checkpoint payload is unavailable (missing or unreadable), not finalising",
                jobRun.Id);

            return BatchImportFinalizationResult.CheckpointUnavailable;
        }

        BatchImportFinalizationResult result = await _finalizer.FinalizeAsync(jobRun, cancellationToken);

        if (result == BatchImportFinalizationResult.Finalized)
        {
            await _watermarkWriter.WriteAsync(jobRun, payload, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Resolves the expected batch count from the run's own checkpoint, for the case where a batch
    /// result reaches the aggregator before the completion event has written the threshold onto the
    /// JobRun. Returns the checkpoint payload and its count, or an unresolved outcome when the payload
    /// is unavailable or its fetch outcome is still open, which keeps the run untouched and eligible
    /// for the watchdog instead of declaring a partial import complete. The payload is handed back so
    /// finalisation can reuse it instead of reading the same row twice.
    /// </summary>
    private async Task<CheckpointThreshold> ResolveCheckpointThresholdAsync(
        JobRun jobRun, CancellationToken cancellationToken)
    {
        JobCheckpoint? checkpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRun.Id, cancellationToken);

        if (checkpoint == null)
        {
            // The batch result overtook the orchestrator's very first checkpoint write, so neither
            // the threshold nor the fetch outcome exists yet.
            _logger.LogInformation(
                "JobRun {JobRunId}: no checkpoint yet (the run is still starting), not finalising",
                jobRun.Id);

            return new CheckpointThreshold(null, 0, BatchImportFinalizationResult.NotReady);
        }

        OrdersImportCheckpointPayload? payload = OrdersImportCheckpointStore
            .TryDeserializePayload(jobRun.Id, checkpoint.Payload);

        if (payload == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: checkpoint payload is unreadable, not finalising",
                jobRun.Id);

            return new CheckpointThreshold(null, 0, BatchImportFinalizationResult.CheckpointUnavailable);
        }

        if (payload.Phase != OrdersImportCheckpointPhase.FetchCompleted)
        {
            // OrdersBatchPublisher.PublishUnitAsync persists an InProgress checkpoint after every
            // published unit, carrying the batch count accumulated so far. Finalising on it would
            // freeze the counters of a multi-unit backfill at the days fetched so far, so the
            // fetch phase — never the batch count alone — gates this.
            _logger.LogInformation(
                "JobRun {JobRunId}: fetch still in progress, {PublishedBatches} batch(es) published, not finalising",
                jobRun.Id,
                payload.TotalBatches);

            return new CheckpointThreshold(null, 0, BatchImportFinalizationResult.NotReady);
        }

        // The fetch finished, yet the JobRun carries no threshold: the completion event was delayed
        // between the orchestrator and this consumer. The checkpoint it was built from is
        // authoritative, so finalise from it rather than leaving the run for the watchdog.
        _logger.LogInformation(
            "JobRun {JobRunId}: checkpoint reports a finished fetch but the expected batch count was never recorded (completion event delayed), finalising from the checkpoint with {ExpectedBatches} batch(es)",
            jobRun.Id,
            payload.TotalBatches);

        return new CheckpointThreshold(payload, payload.TotalBatches, BatchImportFinalizationResult.NotReady);
    }

    private async Task<JobRun?> GetJobRunAsync(Guid jobRunId, string reason, CancellationToken cancellationToken)
    {
        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(jobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found for {Reason}, skipping", jobRunId, reason);
        }

        return jobRun;
    }

    private static bool IsTerminalStatus(JobStatus status)
    {
        return status is JobStatus.Success or JobStatus.Failed or JobStatus.Cancelled;
    }

    private readonly record struct CheckpointThreshold(
        OrdersImportCheckpointPayload? Payload,
        int ExpectedBatchCount,
        BatchImportFinalizationResult Outcome)
    {
        public bool IsKnown => Payload != null;
    }
}
