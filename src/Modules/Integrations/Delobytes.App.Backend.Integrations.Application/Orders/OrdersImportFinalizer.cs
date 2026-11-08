using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Writes the final counters and status of an orders import run once every expected batch result is
/// recorded. The counters are an aggregate over every batch, so they are written here exactly once
/// and never mid-run.
/// </summary>
public sealed class OrdersImportFinalizer
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OrdersImportFinalizer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersImportFinalizer"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="timeProvider">Time provider.</param>
    /// <param name="logger">Logger instance.</param>
    public OrdersImportFinalizer(
        IJobRunRepository jobRunRepository,
        TimeProvider timeProvider,
        ILogger<OrdersImportFinalizer> logger)
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
    /// <see cref="OrdersImportFinalizationResult.Finalized"/> when this call wrote the result, or
    /// <see cref="OrdersImportFinalizationResult.AlreadyFinalized"/> when a concurrent finaliser won
    /// the row first.
    /// </returns>
    public async Task<OrdersImportFinalizationResult> FinalizeAsync(
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
            _logger.LogError("Error importing orders batch: {Message}", totals.CombinedErrors);
        }

        bool updated = await _jobRunRepository.TryUpdateAsync(jobRun, cancellationToken);

        if (!updated)
        {
            // JobRun.RowVersion (xmin) is the concurrency token: the UPDATE above carried the row
            // version loaded at the start of this call. Zero rows were affected because the other
            // entry point — processed concurrently against the same recordedBatches threshold —
            // already finalised the job first and published OrdersImportCompletedEvent. Stepping
            // aside here avoids a double finalisation.
            _logger.LogInformation(
                "JobRun {JobRunId} was already finalised concurrently, skipping",
                jobRun.Id);

            return OrdersImportFinalizationResult.AlreadyFinalized;
        }

        _logger.LogInformation(
            "JobRun {JobRunId} finalised with status {Status}, total Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            jobRun.Id,
            jobRun.Status,
            jobRun.RecordsCreated,
            jobRun.RecordsUpdated,
            jobRun.RecordsSkipped,
            jobRun.RecordsFailed);

        return OrdersImportFinalizationResult.Finalized;
    }
}
