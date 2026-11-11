using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Products;

/// <summary>
/// Products-import adapter over <see cref="ImportRunCompletion"/>: it knows only what is specific to
/// this path — that the terminal batch is the delivery carrying the total batch count, that a
/// successful run advances the sync checkpoint and the connection's last sync time, and that it is
/// the only place that may ask Catalog to reconcile the channel's full listing.
/// </summary>
public class ProductsImportBatchAggregator
{
    private readonly ISyncCheckpointRepository _syncCheckpointRepository;
    private readonly IConnectionRepository _connectionRepository;
    private readonly ImportRunCompletion _completion;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<ProductsImportBatchAggregator> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductsImportBatchAggregator"/> class.
    /// </summary>
    /// <param name="syncCheckpointRepository">Sync checkpoint repository, holds the watermark.</param>
    /// <param name="connectionRepository">Connection repository.</param>
    /// <param name="completion">Shared batch-completion rules.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Time provider.</param>
    public ProductsImportBatchAggregator(
        ISyncCheckpointRepository syncCheckpointRepository,
        IConnectionRepository connectionRepository,
        ImportRunCompletion completion,
        IEventPublisher eventPublisher,
        ILogger<ProductsImportBatchAggregator> logger,
        TimeProvider timeProvider)
    {
        _syncCheckpointRepository = syncCheckpointRepository;
        _connectionRepository = connectionRepository;
        _completion = completion;
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

        // Only the terminal batch knows the final batch count, so only it announces the threshold.
        ImportBatchDelivery delivery = new ImportBatchDelivery(
            JobRunId: message.JobRunId,
            MessageId: messageId,
            RecordsProcessed: message.RecordsProcessed,
            RecordsCreated: message.RecordsCreated,
            RecordsUpdated: message.RecordsUpdated,
            RecordsSkipped: message.RecordsSkipped,
            RecordsFailed: message.RecordsFailed,
            ErrorMessage: message.ErrorMessage,
            IsLastBatch: message.IsLastBatch,
            ExpectedBatchCount: message.IsLastBatch ? message.TotalBatches : null,
            FallbackBatchCount: null,
            CanFinalize: true,
            RepublishesCompletionForRedelivery: message.IsLastBatch);

        ImportRunResult? result = await _completion.RecordBatchAsync(delivery, cancellationToken);

        if (result == null)
        {
            return;
        }

        Connection? connection = null;

        if (result.IsFinalized)
        {
            connection = await AdvanceSyncCheckpointAsync(result.JobRun, cancellationToken);
        }

        if (result.ShouldPublishCompletedEvent)
        {
            await PublishCompletedEventAsync(result.JobRun, cancellationToken);
        }

        if (!result.IsFinalized)
        {
            return;
        }

        await PublishFullSetReconciliationAsync(result.JobRun, connection, cancellationToken);
    }

    /// <summary>
    /// Moves the watermark forward for a finalised run and reports the connection it belongs to.
    /// The watermark only advances on an actually successful run: a Failed/Cancelled run must not
    /// move SyncCheckpoint forward, or the next run would skip records this one never confirmed.
    /// </summary>
    private async Task<Connection?> AdvanceSyncCheckpointAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
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

        Connection? connection = await _connectionRepository.FindByIdAsync(
            jobRun.ConnectionId, cancellationToken);

        if (connection != null)
        {
            connection.LastSyncAt = _timeProvider.GetUtcNow();
            await _connectionRepository.SaveChangesAsync(cancellationToken);
        }

        return connection;
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
}
