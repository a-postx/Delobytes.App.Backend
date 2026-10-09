using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Aggregates batch results and finalises the SyncJob once every expected batch has been
/// recorded (see <see cref="SyncJob.TotalImportBatches"/>) — not simply when the batch flagged
/// IsLastBatch happens to arrive, since a small trailing batch can finish before larger sibling
/// batches still in flight. Idempotent: duplicate deliveries are skipped via MessageId
/// deduplication, and concurrent finalisation attempts are serialised via the Status
/// concurrency token on SyncJob.
/// </summary>
public class ProcessProductImportBatchCompletedConsumer
{
    private readonly ISyncJobRepository _syncJobRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProcessProductImportBatchCompletedConsumer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessProductImportBatchCompletedConsumer"/> class.
    /// </summary>
    public ProcessProductImportBatchCompletedConsumer(
        ISyncJobRepository syncJobRepository,
        IEventPublisher eventPublisher,
        ILogger<ProcessProductImportBatchCompletedConsumer> logger)
    {
        _syncJobRepository = syncJobRepository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Processes the ProductImportBatchCompletedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="messageId">MassTransit MessageId used as idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(
        ProductImportBatchCompletedEvent message,
        Guid messageId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessProductImportBatchCompletedConsumer: SyncJobId={SyncJobId}, MessageId={MessageId}, IsLastBatch={IsLastBatch}, RecordsProcessed={RecordsProcessed}",
            message.SyncJobId,
            messageId,
            message.IsLastBatch,
            message.RecordsProcessed);

        SyncJob? syncJob = await _syncJobRepository.FindByIdAsync(message.SyncJobId, cancellationToken);

        if (syncJob == null)
        {
            _logger.LogWarning(
                "SyncJob {SyncJobId} not found for batch result MessageId={MessageId}, skipping",
                message.SyncJobId,
                messageId);
            return;
        }

        // Cancelled jobs should not accumulate results — drop silently.
        if (syncJob.Status == SyncJobStatus.Cancelled)
        {
            _logger.LogInformation(
                "SyncJob {SyncJobId} is Cancelled, dropping batch result MessageId={MessageId}",
                message.SyncJobId,
                messageId);
            return;
        }

        // Idempotency: skip if this MessageId was already recorded.
        bool alreadyProcessed = await _syncJobRepository.BatchResultExistsAsync(messageId, cancellationToken);

        if (alreadyProcessed)
        {
            _logger.LogInformation(
                "Batch result MessageId={MessageId} for SyncJob {SyncJobId} already recorded, skipping",
                messageId,
                message.SyncJobId);

            // If this is a redelivered last-batch and the job is already terminal, re-publish
            // ProductsImportCompletedEvent so downstream consumers don't get stuck.
            if (message.IsLastBatch && IsTerminalStatus(syncJob.Status))
            {
                await PublishCompletedEventAsync(syncJob, cancellationToken);
            }

            return;
        }

        SyncJobBatchResult batchResult = new SyncJobBatchResult
        {
            Id = Guid.NewGuid(),
            SyncJobId = message.SyncJobId,
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

        _syncJobRepository.AddBatchResult(batchResult);

        // The terminal batch is the only one that knows the final batch count (see
        // ProcessProductsImportConsumer.PublishBatchAsync). Persist it on first sight so that
        // later-arriving non-terminal batches — including ones that complete after this one —
        // can themselves detect "all batches are in" and finalise the job, instead of only the
        // batch literally flagged IsLastBatch being allowed to do so.
        if (message.IsLastBatch)
        {
            syncJob.TotalImportBatches = message.TotalBatches;
            _syncJobRepository.Update(syncJob);
        }

        await _syncJobRepository.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Recorded batch result for SyncJob {SyncJobId}, MessageId={MessageId}",
            message.SyncJobId,
            messageId);

        // Total batch count still unknown: the terminal batch (possibly a small trailing one)
        // hasn't been reported by anyone yet, so this SyncJob cannot be finalised regardless of
        // how many batch results have already been recorded.
        if (syncJob.TotalImportBatches == null)
        {
            return;
        }

        int recordedBatches = await _syncJobRepository.CountBatchResultsAsync(message.SyncJobId, cancellationToken);

        if (recordedBatches < syncJob.TotalImportBatches.Value)
        {
            // Other batches are still being processed; whichever one brings the count up to
            // TotalImportBatches will finalise the job — not necessarily this one, and not
            // necessarily the one flagged IsLastBatch.
            _logger.LogInformation(
                "SyncJob {SyncJobId}: {Recorded}/{Total} batch results recorded, waiting for the rest",
                syncJob.Id,
                recordedBatches,
                syncJob.TotalImportBatches.Value);

            return;
        }

        // All expected batches are recorded — aggregate all stored results and finalise the job.
        SyncJobBatchResultTotals totals = await _syncJobRepository.GetBatchResultTotalsAsync(
            message.SyncJobId, cancellationToken);

        syncJob.RecordsProcessed = totals.TotalProcessed;
        syncJob.RecordsCreated = totals.TotalCreated;
        syncJob.RecordsUpdated = totals.TotalUpdated;
        syncJob.RecordsSkipped = totals.TotalSkipped;
        syncJob.RecordsFailed = totals.TotalFailed;
        syncJob.RecordsImported = totals.TotalCreated + totals.TotalUpdated;
        syncJob.CompletedAt = DateTimeOffset.UtcNow;

        bool hasErrors = totals.TotalFailed > 0 || totals.CombinedErrors != null;
        bool hasSuccesses = totals.TotalCreated > 0 || totals.TotalUpdated > 0 || totals.TotalSkipped > 0;

        if (!hasErrors)
        {
            syncJob.Status = SyncJobStatus.Success;
        }
        else if (hasSuccesses)
        {
            syncJob.Status = SyncJobStatus.PartiallySucceeded;
            syncJob.ErrorMessage = totals.CombinedErrors;
        }
        else
        {
            // All records failed or a fatal error occurred.
            syncJob.Status = SyncJobStatus.Failed;
            syncJob.ErrorMessage = totals.CombinedErrors;
            _logger.LogError("Error importing products batch: {Message}", totals.CombinedErrors);
        }

        _syncJobRepository.Update(syncJob);

        try
        {
            await _syncJobRepository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Status is configured as a concurrency token (SyncJobConfiguration): the UPDATE
            // statement above carried "WHERE Status = 'Running'" built from the value loaded at
            // the start of this call. Zero rows were affected because another batch-completed
            // event — processed concurrently against the same recordedBatches threshold — already
            // flipped the status first and published ProductsImportCompletedEvent. Stepping aside
            // here avoids a double finalisation / duplicate completion event.
            _logger.LogInformation(
                "SyncJob {SyncJobId} was already finalised by a concurrently processed batch result, skipping",
                syncJob.Id);

            return;
        }

        _logger.LogInformation(
            "SyncJob {SyncJobId} finalised with status {Status}. Processed={Processed}, Created={Created}, Updated={Updated}, Skipped={Skipped}, Failed={Failed}",
            syncJob.Id,
            syncJob.Status,
            syncJob.RecordsProcessed,
            syncJob.RecordsCreated,
            syncJob.RecordsUpdated,
            syncJob.RecordsSkipped,
            syncJob.RecordsFailed);

        await PublishCompletedEventAsync(syncJob, cancellationToken);
    }

    private async Task PublishCompletedEventAsync(SyncJob syncJob, CancellationToken cancellationToken)
    {
        try
        {
            ProductsImportCompletedEvent completedEvent = new ProductsImportCompletedEvent
            {
                SyncJobId = syncJob.Id,
                IsSuccess = syncJob.Status == SyncJobStatus.Success
                    || syncJob.Status == SyncJobStatus.PartiallySucceeded,
                ErrorMessage = syncJob.ErrorMessage,
                TotalRecordsProcessed = syncJob.RecordsProcessed,
                TotalRecordsCreated = syncJob.RecordsCreated,
                TotalRecordsUpdated = syncJob.RecordsUpdated,
                TotalRecordsSkipped = syncJob.RecordsSkipped,
                TotalRecordsFailed = syncJob.RecordsFailed,
            };

            await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

            _logger.LogInformation(
                "Published ProductsImportCompletedEvent for SyncJob {SyncJobId}",
                syncJob.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish ProductsImportCompletedEvent for SyncJob {SyncJobId}",
                syncJob.Id);
        }
    }

    private static bool IsTerminalStatus(SyncJobStatus status)
    {
        return status == SyncJobStatus.Success
            || status == SyncJobStatus.Failed
            || status == SyncJobStatus.PartiallySucceeded
            || status == SyncJobStatus.Cancelled;
    }
}
