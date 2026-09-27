using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
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

        IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate.Code);

        // WildberriesApiClient requires template id to resolve endpoint URL.
        if (apiClient is WildberriesApiClient wbClient)
        {
            wbClient.SetTemplateId(connection.SystemChannelTemplateId);
        }

        int batchSize = _options.BatchSize > 0 ? _options.BatchSize : 50;
        int batchNumber = 0;

        // Resume from saved cursor when redelivered mid-import.
        ProductCardsCursor? cursor = DeserializeCursor(syncJob.NextCursor);

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

                bool isLastBatch = data.NextCursor == null || cards.Count == 0;
                batchNumber++;

                _logger.LogInformation(
                    "SyncJob {SyncJobId}: publishing batch {Batch} with {Count} cards, isLast={IsLast}",
                    syncJob.Id,
                    batchNumber,
                    cards.Count,
                    isLastBatch);

                ProductImportBatchRequestedEvent batchEvent = new ProductImportBatchRequestedEvent
                {
                    SyncJobId = syncJob.Id,
                    ConnectionId = connection.Id,
                    ChannelId = connection.ChannelId,
                    Cards = cards,
                    IsLastBatch = isLastBatch,
                };

                await _eventPublisher.PublishAsync(batchEvent, cancellationToken);

                // Save cursor checkpoint so redelivery resumes from the right place.
                syncJob.NextCursor = SerializeCursor(data.NextCursor);
                _syncJobRepository.Update(syncJob);
                await _syncJobRepository.SaveChangesAsync(cancellationToken);

                if (isLastBatch)
                {
                    break;
                }

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
}
