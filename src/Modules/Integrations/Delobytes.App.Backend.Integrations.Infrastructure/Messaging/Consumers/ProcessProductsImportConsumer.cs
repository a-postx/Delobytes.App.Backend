using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for orchestrating products import with cursor-based pagination.
/// Fetches all pages from Wildberries API and publishes a batch event per page.
/// </summary>
public class ProcessProductsImportConsumer
{
    private readonly ISyncJobRepository _syncJobRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly WildberriesImportOptions _options;
    private readonly ILogger<ProcessProductsImportConsumer> _logger;

    public ProcessProductsImportConsumer(
        ISyncJobRepository syncJobRepository,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        IOptions<WildberriesImportOptions> options,
        ILogger<ProcessProductsImportConsumer> logger)
    {
        _syncJobRepository = syncJobRepository;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
        _options = options.Value;
        _logger = logger;
    }

    public async Task ProcessAsync(ProductsImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProcessProductsImportConsumer: SyncJobId={SyncJobId}, ConnectionId={ConnectionId}",
            message.SyncJobId,
            message.ConnectionId);

        SyncJob? syncJob = await _syncJobRepository.FindByIdWithConnectionAsync(message.SyncJobId, cancellationToken);

        if (syncJob == null)
        {
            _logger.LogWarning("SyncJob {SyncJobId} not found, skipping", message.SyncJobId);
            return;
        }

        // Idempotent: already running means redelivery — safe to continue from saved cursor.
        if (syncJob.Status == SyncJobStatus.Cancelled)
        {
            _logger.LogInformation("SyncJob {SyncJobId} is Cancelled, skipping", message.SyncJobId);
            return;
        }

        if (syncJob.Status != SyncJobStatus.Pending && syncJob.Status != SyncJobStatus.Running)
        {
            _logger.LogWarning(
                "SyncJob {SyncJobId} is in terminal status {Status}, skipping",
                message.SyncJobId,
                syncJob.Status);
            return;
        }

        Connection connection = syncJob.Connection;

        if (connection == null || !connection.IsActive)
        {
            string reason = connection == null ? "Connection not found" : "Connection is inactive";
            _logger.LogError(
                "SyncJob {SyncJobId}: {Reason}. Marking as Failed.",
                message.SyncJobId,
                reason);

            await FailJobAsync(syncJob, reason, cancellationToken);
            return;
        }

        // Transition Pending → Running (idempotent: Running stays Running).
        if (syncJob.Status == SyncJobStatus.Pending)
        {
            syncJob.Status = SyncJobStatus.Running;
            syncJob.StartedAt = DateTimeOffset.UtcNow;
            _syncJobRepository.Update(syncJob);
            await _syncJobRepository.SaveChangesAsync(cancellationToken);
        }

        IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate);

        int batchSize = _options.BatchSize > 0 ? _options.BatchSize : 50;
        int batchNumber = 0;

        // Resume from saved cursor when redelivered mid-import.
        ProductCardsCursor? cursor = DeserializeCursor(syncJob.NextCursor);

        // Look-ahead buffer: a non-empty page is held back until the next API response proves
        // whether it was terminal. Without this, WB's empty trailing page (returned once the
        // cursor is exhausted) becomes its own instant batch that reaches the aggregator with
        // IsLastBatch=true and zero counters — before the previous batch, still busy
        // downloading photos, has had a chance to report.
        PendingBatch? bufferedBatch = null;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                ApiResponse<ProductCardsData> apiResponse = await apiClient.GetProductCardsAsync(
                    cursor,
                    batchSize,
                    cancellationToken);

                if (!apiResponse.IsSuccess)
                {
                    string errorMsg = apiResponse.ErrorMessage ?? "Unknown API error";

                    _logger.LogError(
                        "SyncJob {SyncJobId}: WB API call failed (batch {Batch}): {Error}",
                        syncJob.Id,
                        batchNumber,
                        errorMsg);

                    await FailJobAsync(syncJob, errorMsg, cancellationToken);
                    return;
                }

                ProductCardsData data = apiResponse.Data!;
                List<WildberriesCardSnapshot> cards = data.Cards
                    .ToList();

                bool isTerminalPage = data.NextCursor == null;

                // A page carrying no cards but advertising a cursor is meaningless for the
                // import; advance without publishing anything.
                if (cards.Count == 0 && !isTerminalPage)
                {
                    _logger.LogInformation(
                        "SyncJob {SyncJobId}: page {Batch} has no cards but a next cursor, advancing",
                        syncJob.Id,
                        batchNumber);

                    // Nothing is published, so the checkpoint may move past this page safely:
                    // the buffered batch keeps its own cursor and stays refetchable.
                    syncJob.NextCursor = SerializeCursor(data.NextCursor);
                    _syncJobRepository.Update(syncJob);
                    await _syncJobRepository.SaveChangesAsync(cancellationToken);

                    cursor = data.NextCursor;
                    continue;
                }

                if (cards.Count == 0)
                {
                    // Empty terminal page: a signal, not a batch of its own. The page held in the
                    // buffer was the real last one and is published as such; if nothing was ever
                    // buffered the job genuinely has no records, and a single empty terminal batch
                    // is still published so the aggregator can finalise it. Either way exactly one
                    // terminal batch reaches the aggregator, and it is never empty while a real
                    // batch is still downloading photos.
                    List<WildberriesCardSnapshot> terminalCards =
                        bufferedBatch != null ? bufferedBatch.Cards : new List<WildberriesCardSnapshot>();

                    batchNumber++;

                    _logger.LogInformation(
                        "SyncJob {SyncJobId}: publishing terminal batch {Batch} with {Count} cards",
                        syncJob.Id,
                        batchNumber,
                        terminalCards.Count);

                    await PublishBatchAsync(
                        syncJob,
                        connection,
                        terminalCards,
                        isLastBatch: true,
                        batchNumber,
                        cancellationToken);

                    syncJob.NextCursor = null;
                    _syncJobRepository.Update(syncJob);
                    await _syncJobRepository.SaveChangesAsync(cancellationToken);

                    break;
                }

                if (isTerminalPage)
                {
                    // Non-empty page without a cursor: it is itself the last batch. The buffered
                    // page, if any, is known not to be terminal and goes out first.
                    if (bufferedBatch != null)
                    {
                        batchNumber++;

                        await PublishBatchAsync(
                            syncJob,
                            connection,
                            bufferedBatch.Cards,
                            isLastBatch: false,
                            batchNumber,
                            cancellationToken);
                    }

                    batchNumber++;

                    await PublishBatchAsync(
                        syncJob,
                        connection,
                        cards,
                        isLastBatch: true,
                        batchNumber,
                        cancellationToken);

                    syncJob.NextCursor = null;
                    _syncJobRepository.Update(syncJob);
                    await _syncJobRepository.SaveChangesAsync(cancellationToken);

                    break;
                }

                // Non-terminal non-empty page: the buffered page is now known not to be terminal,
                // so it can be published as a regular batch, and the current page takes its place
                // in the buffer until the next response decides its fate.
                if (bufferedBatch != null)
                {
                    batchNumber++;

                    await PublishBatchAsync(
                        syncJob,
                        connection,
                        bufferedBatch.Cards,
                        isLastBatch: false,
                        batchNumber,
                        cancellationToken);

                    // Checkpoint the cursor of the page just published — not of the page just
                    // fetched. A redelivery then refetches the page still sitting in the buffer,
                    // which at worst duplicates one batch that batch idempotency absorbs, instead
                    // of skipping it outright.
                    syncJob.NextCursor = SerializeCursor(bufferedBatch.NextCursor);
                    _syncJobRepository.Update(syncJob);
                    await _syncJobRepository.SaveChangesAsync(cancellationToken);
                }

                bufferedBatch = new PendingBatch
                {
                    Cards = cards,
                    NextCursor = data.NextCursor,
                };

                cursor = data.NextCursor;
            }

            _logger.LogInformation(
                "SyncJob {SyncJobId}: all {Batches} batches published successfully",
                syncJob.Id,
                batchNumber);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("SyncJob {SyncJobId}: processing was cancelled", syncJob.Id);
            // Do not change status — let MassTransit redeliver. Cursor is already persisted.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SyncJob {SyncJobId}: unexpected error during import", syncJob.Id);
            await FailJobAsync(syncJob, ex.Message, cancellationToken);
        }
    }

    private async Task PublishBatchAsync(
        SyncJob syncJob,
        Connection connection,
        List<WildberriesCardSnapshot> cards,
        bool isLastBatch,
        int batchNumber,
        CancellationToken cancellationToken)
    {
        ProductImportBatchRequestedEvent batchEvent = new ProductImportBatchRequestedEvent
        {
            SyncJobId = syncJob.Id,
            ConnectionId = connection.Id,
            ChannelId = connection.ChannelId,
            Cards = cards,
            IsLastBatch = isLastBatch,
        };

        await _eventPublisher.PublishAsync(batchEvent, cancellationToken);

        _logger.LogInformation(
            "SyncJob {SyncJobId}: batch {Batch} published with {Count} cards, IsLastBatch={IsLastBatch}",
            syncJob.Id,
            batchNumber,
            cards.Count,
            isLastBatch);
    }

    private async Task FailJobAsync(SyncJob syncJob, string errorMessage, CancellationToken cancellationToken)
    {
        syncJob.Status = SyncJobStatus.Failed;
        syncJob.ErrorMessage = errorMessage;
        syncJob.CompletedAt = DateTimeOffset.UtcNow;
        _syncJobRepository.Update(syncJob);
        await _syncJobRepository.SaveChangesAsync(cancellationToken);

        try
        {
            ProductsImportCompletedEvent completedEvent = new ProductsImportCompletedEvent
            {
                SyncJobId = syncJob.Id,
                IsSuccess = false,
                ErrorMessage = errorMessage,
                TotalRecordsProcessed = syncJob.RecordsProcessed,
                TotalRecordsCreated = syncJob.RecordsCreated,
                TotalRecordsUpdated = syncJob.RecordsUpdated,
                TotalRecordsSkipped = syncJob.RecordsSkipped,
                TotalRecordsFailed = syncJob.RecordsFailed,
            };

            await _eventPublisher.PublishAsync(completedEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "SyncJob {SyncJobId}: failed to publish ProductsImportCompletedEvent after job failure",
                syncJob.Id);
        }
    }

    private static ProductCardsCursor? DeserializeCursor(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ProductCardsCursor>(json);
        }
        catch
        {
            return null;
        }
    }

    private static string? SerializeCursor(ProductCardsCursor? cursor)
    {
        if (cursor == null)
        {
            return null;
        }

        return JsonSerializer.Serialize(cursor);
    }

    /// <summary>
    /// A fetched page that has not been published yet. It is held back until the next API
    /// response proves whether it was the terminal page. <see cref="NextCursor"/> is the cursor
    /// that produced this page, so a checkpoint always refers to the last published batch.
    /// </summary>
    private class PendingBatch
    {
        public List<WildberriesCardSnapshot> Cards { get; set; } = new();

        public ProductCardsCursor? NextCursor { get; set; }
    }
}
