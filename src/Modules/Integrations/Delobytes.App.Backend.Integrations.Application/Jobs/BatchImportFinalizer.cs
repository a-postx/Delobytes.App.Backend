using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// Writes the final counters and status of a batch-driven import run once every expected batch
/// result is recorded. The counters are an aggregate over every batch, so they are written here
/// exactly once and never mid-run. Shared by the orders and products import paths: both aggregate
/// <see cref="JobRunBatchResult"/> rows onto the same <see cref="JobRun"/>.
/// </summary>
public sealed class BatchImportFinalizer
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<BatchImportFinalizer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchImportFinalizer"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public BatchImportFinalizer(
        IJobRunRepository jobRunRepository,
        TimeProvider timeProvider,
        ILogger<BatchImportFinalizer> logger)
    {
        _jobRunRepository = jobRunRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// Computes and persists the final counters and status of the run.
    /// </summary>
    /// <param name="jobRun">Run to finalise, loaded by the caller.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="BatchImportFinalizationResult.Finalized"/> when this call wrote the result, or
    /// <see cref="BatchImportFinalizationResult.AlreadyFinalized"/> when a concurrent finaliser won
    /// the row first.
    /// </returns>
    public async Task<BatchImportFinalizationResult> FinalizeAsync(
        JobRun jobRun, CancellationToken cancellationToken)
    {
        JobRunBatchResultTotals totals = await _jobRunRepository.GetBatchResultTotalsAsync(
            jobRun.Id, cancellationToken);

        jobRun.RecordsCreated = totals.TotalCreated;
        jobRun.RecordsUpdated = totals.TotalUpdated;
        jobRun.RecordsSkipped = totals.TotalSkipped;
        jobRun.RecordsFailed = totals.TotalFailed;
        jobRun.CompletedAt = _timeProvider.GetUtcNow();

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
            _logger.LogError(
                "Error importing batch for JobRun {JobRunId}: {Message}",
                jobRun.Id,
                totals.CombinedErrors);
        }

        bool updated = await _jobRunRepository.TryUpdateAsync(jobRun, cancellationToken);

        if (!updated)
        {
            // JobRun.RowVersion (xmin) is the concurrency token: the UPDATE above carried the row
            // version loaded at the start of this call. Zero rows were affected because the other
            // entry point — processed concurrently against the same recordedBatches threshold —
            // already finalised the job first and published its completion event. Stepping aside
            // here avoids a double finalisation.
            _logger.LogInformation(
                "JobRun {JobRunId} was already finalised concurrently, skipping",
                jobRun.Id);

            return BatchImportFinalizationResult.AlreadyFinalized;
        }

        _logger.LogInformation(
            "JobRun {JobRunId} finalised with status {Status}, total Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            jobRun.Id,
            jobRun.Status,
            jobRun.RecordsCreated,
            jobRun.RecordsUpdated,
            jobRun.RecordsSkipped,
            jobRun.RecordsFailed);

        return BatchImportFinalizationResult.Finalized;
    }
}
