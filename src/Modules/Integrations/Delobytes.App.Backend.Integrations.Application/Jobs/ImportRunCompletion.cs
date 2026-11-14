using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// Decides when a batch-driven import run is complete: records each delivered batch result under
/// its idempotency key, learns how many results the run expects, counts what is recorded and, once
/// the count reaches the expectation, finalises the run exactly once through
/// <see cref="BatchImportFinalizer"/>. Shared by every batch-driven import path, so the race rules
/// — duplicate delivery, a threshold announced before or after the results it describes, and a
/// concurrent finaliser — exist in one place instead of once per path.
/// </summary>
public sealed class ImportRunCompletion
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly BatchImportFinalizer _finalizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ImportRunCompletion> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportRunCompletion"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="finalizer">Finaliser that writes the run's final counters and status.</param>
    /// <param name="timeProvider">Clock source.</param>
    /// <param name="logger">Logger instance.</param>
    public ImportRunCompletion(
        IJobRunRepository jobRunRepository,
        BatchImportFinalizer finalizer,
        TimeProvider timeProvider,
        ILogger<ImportRunCompletion> logger)
    {
        _jobRunRepository = jobRunRepository;
        _finalizer = finalizer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Records one batch result, learning the expected batch count when the delivery announces it,
    /// then finalises the run if every expected result is recorded.
    /// </summary>
    /// <param name="delivery">Batch result as delivered by the transport.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run and what the caller still owes downstream consumers, or <c>null</c> when the run is gone.</returns>
    public async Task<ImportRunResult?> RecordBatchAsync(
        ImportBatchDelivery delivery,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(delivery.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} not found for batch result, skipping",
                delivery.JobRunId);

            return null;
        }

        if (await _jobRunRepository.BatchResultExistsAsync(delivery.MessageId, cancellationToken))
        {
            _logger.LogInformation(
                "Batch result MessageId={MessageId} for JobRun {JobRunId} already recorded, skipping",
                delivery.MessageId,
                delivery.JobRunId);

            // A redelivery of the delivery that finishes the run still has to nudge downstream
            // consumers, which may have missed the first completion event.
            bool republish = delivery.RepublishesCompletionForRedelivery && IsTerminalStatus(jobRun.Status);

            return new ImportRunResult(jobRun, false, republish);
        }

        _jobRunRepository.AddBatchResult(new JobRunBatchResult
        {
            Id = Guid.NewGuid(),
            JobRunId = delivery.JobRunId,
            MessageId = delivery.MessageId,
            RecordsProcessed = delivery.RecordsProcessed,
            RecordsCreated = delivery.RecordsCreated,
            RecordsUpdated = delivery.RecordsUpdated,
            RecordsSkipped = delivery.RecordsSkipped,
            RecordsFailed = delivery.RecordsFailed,
            ErrorMessage = TruncateString(delivery.ErrorMessage, 2000),
            IsLastBatch = delivery.IsLastBatch,
            ReceivedAt = _timeProvider.GetUtcNow(),
        });

        await _jobRunRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded batch result for JobRun {JobRunId}, MessageId={MessageId}",
            delivery.JobRunId,
            delivery.MessageId);

        int? expectedBatchCount = await _jobRunRepository.GetExpectedBatchCountAsync(
            jobRun.Id, cancellationToken);

        expectedBatchCount ??= delivery.ExpectedBatchCount ?? delivery.FallbackBatchCount;

        if (expectedBatchCount == null)
        {
            _logger.LogInformation(
                "JobRun {JobRunId}: no expected batch count yet, not finalising",
                jobRun.Id);

            return new ImportRunResult(jobRun, false, false);
        }

        int recordedBatches = await _jobRunRepository.CountBatchResultsAsync(jobRun.Id, cancellationToken);

        if (recordedBatches < expectedBatchCount.Value)
        {
            if (delivery.ExpectedBatchCount.HasValue && jobRun.ExpectedBatchCount != delivery.ExpectedBatchCount)
            {
                // The count is durable now, before the remaining results arrive: a redelivery of the
                // announcing batch is not guaranteed, and the next delivery carries no count at all.
                ImportRunResult? lostThresholdRace = await PersistExpectedBatchCountAsync(
                    jobRun, delivery.ExpectedBatchCount.Value, cancellationToken);

                if (lostThresholdRace != null)
                {
                    return lostThresholdRace;
                }
            }

            _logger.LogInformation(
                "JobRun {JobRunId}: {Recorded}/{Expected} batch results recorded, waiting for the rest",
                jobRun.Id,
                recordedBatches,
                expectedBatchCount.Value);

            return new ImportRunResult(jobRun, false, false);
        }

        if (!delivery.CanFinalize)
        {
            // Every announced result is recorded, but the caller cannot yet prove the run was fully
            // observed — e.g. the fetch outcome it has to persist alongside is missing or unreadable.
            // Finalising here would burn the run's only chance to record it.
            _logger.LogWarning(
                "JobRun {JobRunId}: {Recorded}/{Expected} batch results recorded, but the run's outcome is not available, not finalising",
                jobRun.Id,
                recordedBatches,
                expectedBatchCount.Value);

            return new ImportRunResult(jobRun, false, false);
        }

        return await FinishAsync(jobRun, expectedBatchCount.Value, cancellationToken);
    }

    /// <summary>
    /// Records the batch count a run expects, then finalises it if every expected result is
    /// already recorded. Called by paths whose orchestrator announces the count separately from
    /// the batch results.
    /// </summary>
    /// <param name="jobRunId">Run the announcement belongs to.</param>
    /// <param name="totalBatches">Expected number of batch results.</param>
    /// <param name="canFinalize">
    /// Whether the caller can vouch for the run's outcome. <c>false</c> records the count but leaves
    /// the run open, so a later delivery can finalise it once the outcome is readable.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run and what the caller still owes downstream consumers, or <c>null</c> when the run is gone.</returns>
    public async Task<ImportRunResult?> AnnounceExpectedBatchCountAsync(
        Guid jobRunId,
        int totalBatches,
        bool canFinalize,
        CancellationToken cancellationToken)
    {
        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(jobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} not found for the batch-count announcement, skipping",
                jobRunId);

            return null;
        }

        if (IsTerminalStatus(jobRun.Status))
        {
            // A redelivered announcement still has to nudge downstream consumers.
            return new ImportRunResult(jobRun, false, true);
        }

        if (jobRun.ExpectedBatchCount != totalBatches)
        {
            ImportRunResult? lostThresholdRace = await PersistExpectedBatchCountAsync(
                jobRun, totalBatches, cancellationToken);

            if (lostThresholdRace != null)
            {
                return lostThresholdRace;
            }
        }

        int? expectedBatchCount = await _jobRunRepository
            .GetExpectedBatchCountAsync(jobRun.Id, cancellationToken);

        int recordedBatches = await _jobRunRepository.CountBatchResultsAsync(jobRun.Id, cancellationToken);

        if (recordedBatches < (expectedBatchCount ?? totalBatches))
        {
            // Counters are an aggregate over every batch, so a mid-run write would publish a
            // partial total as if it were final. The result may also be read from the run's own
            // checkpoint by the caller, which is why the threshold, not this call, decides.
            _logger.LogInformation(
                "JobRun {JobRunId}: {Recorded}/{Expected} batch results recorded, waiting for the rest",
                jobRun.Id,
                recordedBatches,
                expectedBatchCount ?? totalBatches);

            return new ImportRunResult(jobRun, false, false);
        }

        if (!canFinalize)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: {Recorded}/{Expected} batch results recorded, but the run's outcome is not available, not finalising",
                jobRun.Id,
                recordedBatches,
                expectedBatchCount ?? totalBatches);

            return new ImportRunResult(jobRun, false, false);
        }

        return await FinishAsync(jobRun, expectedBatchCount ?? totalBatches, cancellationToken);
    }

    /// <summary>
    /// Persists an announced batch count on the run, reporting a lost race to a concurrent writer.
    /// </summary>
    /// <returns>
    /// <c>null</c> when the count is persisted and the caller should continue, or the result the
    /// caller must return as is when the run was finalised or replaced concurrently.
    /// </returns>
    private async Task<ImportRunResult?> PersistExpectedBatchCountAsync(
        JobRun jobRun,
        int totalBatches,
        CancellationToken cancellationToken)
    {
        jobRun.ExpectedBatchCount = totalBatches;

        if (await _jobRunRepository.TryUpdateAsync(jobRun, cancellationToken))
        {
            return null;
        }

        // Another writer — usually the delivery that finalised the run — saved first. The state it
        // wrote is the durable one, so continue from it instead of overwriting it.
        JobRun? reloaded = await _jobRunRepository.GetByIdAsync(jobRun.Id, cancellationToken);

        if (reloaded == null)
        {
            return new ImportRunResult(jobRun, false, false);
        }

        if (IsTerminalStatus(reloaded.Status))
        {
            return new ImportRunResult(reloaded, false, false);
        }

        reloaded.ExpectedBatchCount = totalBatches;

        return null;
    }

    /// <summary>
    /// Writes the run's final counters and status, persisting the expected count together with them.
    /// </summary>
    private async Task<ImportRunResult> FinishAsync(
        JobRun jobRun,
        int expectedBatchCount,
        CancellationToken cancellationToken)
    {
        jobRun.ExpectedBatchCount = expectedBatchCount;

        BatchImportFinalizationResult result = await _finalizer.FinalizeAsync(jobRun, cancellationToken);

        if (result == BatchImportFinalizationResult.AlreadyFinalized)
        {
            return new ImportRunResult(jobRun, false, false);
        }

        return new ImportRunResult(jobRun, true, true);
    }

    private static bool IsTerminalStatus(JobStatus status)
    {
        return status is JobStatus.Success or JobStatus.Failed or JobStatus.Cancelled;
    }

    private static string? TruncateString(string? value, int maxLength)
    {
        if (value == null || value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength];
    }
}

/// <summary>
/// One delivered batch result, as the transport reported it.
/// </summary>
/// <param name="JobRunId">Run the batch belongs to.</param>
/// <param name="MessageId">Deterministic identifier used as the idempotency key.</param>
/// <param name="RecordsProcessed">Records the batch processed.</param>
/// <param name="RecordsCreated">Records the batch created.</param>
/// <param name="RecordsUpdated">Records the batch updated.</param>
/// <param name="RecordsSkipped">Records the batch skipped.</param>
/// <param name="RecordsFailed">Records the batch failed.</param>
/// <param name="ErrorMessage">Batch errors, when any.</param>
/// <param name="IsLastBatch">Whether the transport flagged this delivery as the terminal batch.</param>
/// <param name="ExpectedBatchCount">
/// Total batch count this delivery makes known and that is durable beyond this call, or <c>null</c>
/// when it announces none.
/// </param>
/// <param name="FallbackBatchCount">
/// Total batch count the caller can derive from its own durable state but must not write onto the
/// run, or <c>null</c>. Used for counting only.
/// </param>
/// <param name="CanFinalize">
/// Whether the caller can vouch for the run's outcome. <c>false</c> records the result but leaves
/// the run open, so a later delivery can finalise it once the outcome is readable.
/// </param>
/// <param name="RepublishesCompletionForRedelivery">
/// Whether a redelivery of this batch must republish the completion event when the run is already
/// terminal.
/// </param>
public sealed record ImportBatchDelivery(
    Guid JobRunId,
    Guid MessageId,
    int RecordsProcessed,
    int RecordsCreated,
    int RecordsUpdated,
    int RecordsSkipped,
    int RecordsFailed,
    string? ErrorMessage,
    bool IsLastBatch,
    int? ExpectedBatchCount,
    int? FallbackBatchCount,
    bool CanFinalize,
    bool RepublishesCompletionForRedelivery);

/// <summary>
/// Outcome of one call to <see cref="ImportRunCompletion"/>.
/// </summary>
/// <param name="JobRun">Run in its post-call state.</param>
/// <param name="IsFinalized">Whether this call wrote the run's final counters and status.</param>
/// <param name="ShouldPublishCompletedEvent">
/// Whether the caller still owes downstream consumers the run's completion event.
/// </param>
public sealed record ImportRunResult(JobRun JobRun, bool IsFinalized, bool ShouldPublishCompletedEvent);
