using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Aggregates batch results and finalises the JobRun once every expected batch has been
/// recorded (see <see cref="ProductsImportJob.TotalImportBatches"/>) — not simply when the batch
/// flagged IsLastBatch happens to arrive, since a small trailing batch can finish before larger
/// sibling batches still in flight. Idempotent: duplicate deliveries are skipped via MessageId
/// deduplication, and concurrent finalisation attempts are serialised via JobRun's xmin.
/// </summary>
public class ProcessProductImportBatchCompletedConsumer
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProcessProductImportBatchCompletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessProductImportBatchCompletedConsumer"/> class.
    /// </summary>
    public ProcessProductImportBatchCompletedConsumer(
        IJobRunRepository jobRunRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IConnectionRepository connectionRepository,
        IEventPublisher eventPublisher,
        ILogger<ProcessProductImportBatchCompletedConsumer> logger)
    {
        _jobRunRepository = jobRunRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _connectionRepository = connectionRepository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Processes the ProductBatchImportCompletedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="messageId">MassTransit MessageId used as idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(
        ProductBatchImportCompletedEvent message,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessProductImportBatchCompletedConsumer: JobRunId={JobRunId}, MessageId={MessageId}, IsLastBatch={IsLastBatch}, RecordsProcessed={RecordsProcessed}",
            message.JobRunId,
            messageId,
            message.IsLastBatch,
            message.RecordsProcessed);

        JobRun? jobRun = await _jobRunRepository.GetByIdAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} not found for batch result MessageId={MessageId}, skipping",
                message.JobRunId,
                messageId);
            return;
        }

        // Idempotency: skip if this MessageId was already recorded.
        bool alreadyProcessed = await _jobRunRepository.BatchResultExistsAsync(messageId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Batch result MessageId={MessageId} for JobRun {JobRunId} already recorded, skipping",
                messageId,
                message.JobRunId);

            // If this is a redelivered last-batch and the job is already terminal, re-publish
            // ProductsImportCompletedEvent so downstream consumers don't get stuck.
            if (message.IsLastBatch && IsTerminalStatus(jobRun.Status))
            {
                await PublishCompletedEventAsync(jobRun, cancellationToken);
            }

            return;
        }

        // The batch result is still recorded and its counters still accumulated,
        // so a cancelled run reports what it actually completed instead of zeroes.
        JobRunBatchResult batchResult = new JobRunBatchResult
        {
            Id = Guid.NewGuid(),
            JobRunId = message.JobRunId,
            MessageId = messageId,
            RecordsProcessed = message.RecordsProcessed,
            RecordsCreated = message.RecordsCreated,
            RecordsUpdated = message.RecordsUpdated,
            RecordsSkipped = message.RecordsSkipped,
            RecordsFailed = message.RecordsFailed,
            ErrorMessage = message.ErrorMessage,
            IsLastBatch = message.IsLastBatch,
            ReceivedAt = DateTimeOffset.UtcNow,
        };

        _jobRunRepository.AddBatchResult(batchResult);

        ProductsImportJob? importJob = await _jobRunRepository.GetProductsImportJobAsync(jobRun.Id, cancellationToken);

        if (importJob == null)
        {
            _logger.LogError(
                "JobRun {JobRunId}: no ProductsImportJob row found, cannot track TotalImportBatches",
                jobRun.Id);
            return;
        }

        // The terminal batch is the only one that knows the final batch count.
        // Persist it on first sight so that later-arriving non-terminal batches — including ones
        // that complete after this one — can themselves detect "all batches are in".
        if (message.IsLastBatch)
        {
            importJob.TotalImportBatches = message.TotalBatches;
        }

        await _jobRunRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded batch result for JobRun {JobRunId}, MessageId={MessageId}",
            message.JobRunId,
            messageId);

        // Total batch count still unknown: the terminal batch (possibly a small trailing one)
        // hasn't been reported by anyone yet, so this JobRun cannot be finalised regardless of
        // how many batch results have already been recorded.
        if (importJob.TotalImportBatches == null)
        {
            return;
        }

        int recordedBatches = await _jobRunRepository.CountBatchResultsAsync(message.JobRunId, cancellationToken);

        if (recordedBatches < importJob.TotalImportBatches.Value)
        {
            // Other batches are still being processed; whichever one brings the count up to
            // TotalImportBatches will finalise the job — not necessarily this one, and not
            // necessarily the one flagged IsLastBatch.
            _logger.LogInformation(
                "JobRun {JobRunId}: {Recorded}/{Total} batch results recorded, waiting for the rest",
                jobRun.Id,
                recordedBatches,
                importJob.TotalImportBatches.Value);

            return;
        }

        // All expected batches are recorded — aggregate all stored results and finalise the job.
        JobRunBatchResultTotals totals = await _jobRunRepository.GetBatchResultTotalsAsync(
            message.JobRunId, cancellationToken);

        jobRun.RecordsCreated = totals.TotalCreated;
        jobRun.RecordsUpdated = totals.TotalUpdated;
        jobRun.RecordsSkipped = totals.TotalSkipped;
        jobRun.RecordsFailed = totals.TotalFailed;
        jobRun.CompletedAt = DateTimeOffset.UtcNow;

        bool hasErrors = totals.TotalFailed > 0 || totals.CombinedErrors != null;

        if (jobRun.Status == JobStatus.Cancelled)
        {
            // Preserve the cancellation outcome — the counters above already reflect whatever
            // was actually processed before the cooperative stop took effect.
            jobRun.Error = totals.CombinedErrors;
        }
        else if (!hasErrors)
        {
            jobRun.Status = JobStatus.Success;
        }
        else
        {
            // JobStatus has no "partially succeeded" value (unlike the old SyncJobStatus): any
            // failed record makes the run Failed even if other records in the same batch succeeded.
            jobRun.Status = JobStatus.Failed;
            jobRun.Error = totals.CombinedErrors;
            _logger.LogError("Error importing products batch: {Message}", totals.CombinedErrors);
        }

        try
        {
            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // JobRun.RowVersion (xmin) is the concurrency token: the UPDATE above carried the
            // row version loaded at the start of this call. Zero rows were affected because
            // another batch-completed event — processed concurrently against the same
            // recordedBatches threshold — already finalised the job first and published
            // ProductsImportCompletedEvent. Stepping aside here avoids a double finalisation.
            _logger.LogInformation(
                "JobRun {JobRunId} was already finalised by a concurrently processed batch result, skipping",
                jobRun.Id);

            return;
        }

        _logger.LogInformation(
            "JobRun {JobRunId} finalised with status {Status}. Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            jobRun.Id,
            jobRun.Status,
            jobRun.RecordsCreated,
            jobRun.RecordsUpdated,
            jobRun.RecordsSkipped,
            jobRun.RecordsFailed);

        // The watermark only advances on an actually successful run: a Failed/Cancelled run
        // must not move SyncCheckpoint forward, or the next run would skip records this one
        // never confirmed.
        if (jobRun.Status == JobStatus.Success)
        {
            SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository.GetAsync(
                jobRun.ConnectionId, jobRun.JobType, cancellationToken);

            if (syncCheckpoint != null)
            {
                syncCheckpoint.LastSuccessAt = DateTimeOffset.UtcNow;
                syncCheckpoint.LastJobRunId = jobRun.Id;
                await _syncCheckpointRepository.UpsertAsync(syncCheckpoint, cancellationToken);
            }
        }

        Connection? connection = await _connectionRepository.FindByIdAsync(jobRun.ConnectionId, cancellationToken);

        if (connection != null)
        {
            connection.LastSyncAt = DateTimeOffset.UtcNow;
            await _connectionRepository.SaveChangesAsync(cancellationToken);
        }

        await PublishCompletedEventAsync(jobRun, cancellationToken);
    }

    private async Task PublishCompletedEventAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        try
        {
            ProductsImportCompletedEvent completedEvent = new ProductsImportCompletedEvent
            {
                JobRunId = jobRun.Id,
                IsSuccess = jobRun.Status == JobStatus.Success,
                ErrorMessage = jobRun.Error,
                TotalRecordsProcessed = jobRun.RecordsCreated + jobRun.RecordsUpdated + jobRun.RecordsSkipped + jobRun.RecordsFailed,
                TotalRecordsCreated = jobRun.RecordsCreated,
                TotalRecordsUpdated = jobRun.RecordsUpdated,
                TotalRecordsSkipped = jobRun.RecordsSkipped,
                TotalRecordsFailed = jobRun.RecordsFailed,
            };

            await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

            _logger.LogInformation(
                "Published ProductsImportCompletedEvent for JobRun {JobRunId}",
                jobRun.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish ProductsImportCompletedEvent for JobRun {JobRunId}",
                jobRun.Id);
        }
    }

    private static bool IsTerminalStatus(JobStatus status)
    {
        return status == JobStatus.Success
            || status == JobStatus.Failed
            || status == JobStatus.Cancelled;
    }
}
