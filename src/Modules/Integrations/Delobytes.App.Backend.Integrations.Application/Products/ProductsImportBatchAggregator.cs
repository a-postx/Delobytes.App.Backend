using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Products;

/// <summary>
/// Aggregates batch results of a products import run and finalises the JobRun once every expected
/// batch has been recorded (see <see cref="ProductsImportJob.TotalImportBatches"/>) — not simply
/// when the batch flagged IsLastBatch happens to arrive, since a small trailing batch can finish
/// before larger sibling batches still in flight. Idempotent: duplicate deliveries are skipped via
/// MessageId deduplication, and concurrent finalisation attempts are serialised by the shared
/// <see cref="BatchImportFinalizer"/> through JobRun's xmin.
/// </summary>
public class ProductsImportBatchAggregator
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly BatchImportFinalizer _finalizer;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProductsImportBatchAggregator> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductsImportBatchAggregator"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="syncCheckpointRepository">Sync checkpoint repository, holds the watermark.</param>
    /// <param name="connectionRepository">Connection repository.</param>
    /// <param name="finalizer">Finaliser that writes the run's final counters and status.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Time provider.</param>
    public ProductsImportBatchAggregator(
        IJobRunRepository jobRunRepository,
        ISyncCheckpointRepository syncCheckpointRepository,
        IConnectionRepository connectionRepository,
        BatchImportFinalizer finalizer,
        IEventPublisher eventPublisher,
        ILogger<ProductsImportBatchAggregator> logger,
        TimeProvider timeProvider)
    {
        _jobRunRepository = jobRunRepository;
        _syncCheckpointRepository = syncCheckpointRepository;
        _connectionRepository = connectionRepository;
        _finalizer = finalizer;
        _eventPublisher = eventPublisher;
        _logger = logger;
        _timeProvider = timeProvider;
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
            "ProductsImportBatchAggregator: JobRunId={JobRunId}, MessageId={MessageId}, IsLastBatch={IsLastBatch}, RecordsProcessed={RecordsProcessed}",
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
            ReceivedAt = _timeProvider.GetUtcNow(),
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
        BatchImportFinalizationResult finalization = await _finalizer.FinalizeAsync(jobRun, cancellationToken);

        if (finalization == BatchImportFinalizationResult.AlreadyFinalized)
        {
            // JobRun.RowVersion (xmin) is the concurrency token: another batch-completed event —
            // processed concurrently against the same recordedBatches threshold — already finalised
            // the job first and published ProductsImportCompletedEvent. Stepping aside avoids a
            // double finalisation.
            return;
        }

        // The watermark only advances on an actually successful run: a Failed/Cancelled run
        // must not move SyncCheckpoint forward, or the next run would skip records this one
        // never confirmed.
        if (jobRun.Status == JobStatus.Success)
        {
            SyncCheckpoint? syncCheckpoint = await _syncCheckpointRepository.GetAsync(
                jobRun.ConnectionId, jobRun.JobType, cancellationToken);

            if (syncCheckpoint != null)
            {
                syncCheckpoint.LastSuccessAt = _timeProvider.GetUtcNow();
                syncCheckpoint.LastJobRunId = jobRun.Id;
                await _syncCheckpointRepository.UpsertAsync(syncCheckpoint, cancellationToken);
            }
        }

        Connection? connection = await _connectionRepository.FindByIdAsync(jobRun.ConnectionId, cancellationToken);

        if (connection != null)
        {
            connection.LastSyncAt = _timeProvider.GetUtcNow();
            await _connectionRepository.SaveChangesAsync(cancellationToken);
        }

        await PublishCompletedEventAsync(jobRun, cancellationToken);

        await PublishFullSetReconciliationAsync(jobRun, connection, cancellationToken);
    }

    /// <summary>
    /// Asks Catalog to reconcile <c>ChannelProduct.IsActive</c> against the listing this run walked.
    /// </summary>
    /// <remarks>
    /// Fired from here and nowhere else, because this method runs only once every batch of the run
    /// has been recorded — and a batch result reaches the aggregator strictly after that batch
    /// committed in Catalog. That ordering is the entire point: the sweep compares each link's
    /// <c>LastSyncedAt</c> against the run's start, which only answers correctly once every sibling's
    /// stamps are visible. Issued from the batch consumer instead, it read its siblings' uncommitted
    /// rows as dropped cards and deactivated them.
    /// <para>
    /// Only a fully successful run reconciles. A run with any failed record leaves cards unstamped,
    /// and an unstamped card is indistinguishable from one the channel no longer lists; a cancelled
    /// run ends in <see cref="JobStatus.Cancelled"/> and never walked the whole listing. The same
    /// condition covers a missing <c>StartedAt</c>: without a boundary the sweep would deactivate
    /// every link of the channel.
    /// </para>
    /// <para>
    /// Failures are logged and swallowed: the import itself succeeded, and a missed reconciliation
    /// only leaves the flag behind until the next successful run, which reconciles again.
    /// </para>
    /// </remarks>
    /// <param name="jobRun">The finalised job run.</param>
    /// <param name="connection">The connection the run imported through, when it could be resolved.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task PublishFullSetReconciliationAsync(
        JobRun jobRun,
        Connection? connection,
        CancellationToken cancellationToken)
    {
        if (jobRun.Status != JobStatus.Success)
        {
            return;
        }

        if (jobRun.StartedAt == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} succeeded but has no StartedAt; skipping full-set reconciliation, " +
                "since there is no boundary to compare the imported cards against.",
                jobRun.Id);

            return;
        }

        if (connection == null)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: connection {ConnectionId} not found; skipping full-set reconciliation.",
                jobRun.Id,
                jobRun.ConnectionId);

            return;
        }

        try
        {
            ChannelProductReconciliationRequestedEvent reconciliationEvent =
                new ChannelProductReconciliationRequestedEvent
                {
                    JobRunId = jobRun.Id,
                    ConnectionId = connection.Id,
                    ChannelId = connection.ChannelId,
                    RunStartedAt = jobRun.StartedAt.Value,
                };

            await _eventPublisher.PublishAsync(reconciliationEvent, cancellationToken);

            _logger.LogInformation(
                "Published ChannelProductFullSetReconciliationRequestedEvent for JobRun {JobRunId}, ChannelId={ChannelId}, RunStartedAt={RunStartedAt}",
                jobRun.Id,
                connection.ChannelId,
                jobRun.StartedAt.Value);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to publish ChannelProductFullSetReconciliationRequestedEvent for JobRun {JobRunId}",
                jobRun.Id);
        }
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
