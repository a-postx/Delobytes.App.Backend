using System.Text.Json;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Application.Products;

/// <summary>
/// Orchestrates a products import run: walks the channel's product cards page by page, holds each
/// non-empty page back until the next response proves it was terminal, and publishes a batch event
/// per page. Transport concerns stay outside: this class is invoked by the message adapter.
/// </summary>
public class ProductsImportOrchestrator
{
    private readonly IJobRunRepository _jobRunRepository;
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly IChannelApiClientFactory _clientFactory;
    private readonly IEventPublisher _eventPublisher;
    private readonly IJobRunExecutionScope _executionScope;
    private readonly IProductsImportTuningResolver _tunings;
    private readonly ILogger<ProductsImportOrchestrator> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductsImportOrchestrator"/> class.
    /// </summary>
    /// <param name="jobRunRepository">Job run repository.</param>
    /// <param name="jobCheckpointRepository">Job checkpoint repository, holds the page cursor.</param>
    /// <param name="clientFactory">Resolves the API client of the run's channel.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="executionScope">Ambient scope correlating the run's HTTP calls with the JobRun.</param>
    /// <param name="tunings">Resolves the products import tuning of the run's channel.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="timeProvider">Clock source.</param>
    public ProductsImportOrchestrator(
        IJobRunRepository jobRunRepository,
        IJobCheckpointRepository jobCheckpointRepository,
        IChannelApiClientFactory clientFactory,
        IEventPublisher eventPublisher,
        IJobRunExecutionScope executionScope,
        IProductsImportTuningResolver tunings,
        ILogger<ProductsImportOrchestrator> logger,
        TimeProvider timeProvider)
    {
        _jobRunRepository = jobRunRepository;
        _jobCheckpointRepository = jobCheckpointRepository;
        _clientFactory = clientFactory;
        _eventPublisher = eventPublisher;
        _executionScope = executionScope;
        _tunings = tunings;
        _logger = logger;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Processes the ProductsImportRequestedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(ProductsImportRequestedEvent message, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "ProductsImportOrchestrator: JobRunId={JobRunId}, ConnectionId={ConnectionId}",
            message.JobRunId,
            message.ConnectionId);

        JobRun? jobRun = await _jobRunRepository.GetByIdWithConnectionAsync(message.JobRunId, cancellationToken);

        if (jobRun == null)
        {
            _logger.LogWarning("JobRun {JobRunId} not found, skipping", message.JobRunId);
            return;
        }

        // Idempotent: already running means redelivery РІР‚вЂќ safe to continue from the saved checkpoint.
        if (jobRun.Status == JobStatus.Cancelled)
        {
            _logger.LogInformation("JobRun {JobRunId} is Cancelled, skipping", message.JobRunId);
            return;
        }

        if (jobRun.Status != JobStatus.Pending && jobRun.Status != JobStatus.Running)
        {
            _logger.LogWarning(
                "JobRun {JobRunId} is in terminal status {Status}, skipping",
                message.JobRunId,
                jobRun.Status);
            return;
        }

        Connection connection = jobRun.Connection;

        if (connection == null || !connection.IsActive)
        {
            string reason = connection == null ? "Connection not found" : "Connection is inactive";
            _logger.LogError(
                "JobRun {JobRunId}: {Reason}. Marking as Failed.",
                message.JobRunId,
                reason);

            await FailJobAsync(jobRun, reason, cancellationToken);
            return;
        }

        // Transition Pending РІвЂ вЂ™ Running (idempotent: Running stays Running).
        if (jobRun.Status == JobStatus.Pending)
        {
            jobRun.Status = JobStatus.Running;
            jobRun.StartedAt = _timeProvider.GetUtcNow();
            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }

        IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate);

        int configuredBatchSize = _tunings.Resolve(connection.SystemChannelTemplate.Code).BatchSize;
        int batchSize = configuredBatchSize > 0 ? configuredBatchSize : ProductsImportTuning.DefaultBatchSize;
        int batchNumber = 0;

        // Resume from the saved checkpoint cursor when redelivered mid-import.
        JobCheckpoint? checkpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRun.Id, cancellationToken);
        ProductCardsCursor? cursor = DeserializeCursor(checkpoint?.Payload);

        // Look-ahead buffer: a non-empty page is held back until the next API response proves
        // whether it was terminal. Without this, WB's empty trailing page (returned once the
        // cursor is exhausted) becomes its own instant batch that reaches the aggregator with
        // IsLastBatch=true and zero counters РІР‚вЂќ before the previous batch, still busy
        // downloading photos, has had a chance to report.
        PendingBatch? bufferedBatch = null;

        // Correlates every HTTP call made through apiClient below with this JobRun, so
        // RawApiResponseCaptureHandler can attribute captured rows to it. Restored on dispose
        // regardless of outcome.
        using (IDisposable scope = _executionScope.EnterScope(jobRun.Id, connection.Id, connection.ChannelId))
        {
            try
            {
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    ApiResponse<ProductCardsData> apiResponse = await apiClient.GetProductCardsAsync(
                        connection.Id,
                        cursor,
                        batchSize,
                        cancellationToken);

                    if (!apiResponse.IsSuccess)
                    {
                        string errorMsg = apiResponse.ErrorMessage ?? "Unknown API error";

                        _logger.LogError(
                            "JobRun {JobRunId}: WB API call failed (batch {Batch}): {Error}",
                            jobRun.Id,
                            batchNumber,
                            errorMsg);

                        await FailJobAsync(jobRun, errorMsg, cancellationToken);
                        return;
                    }

                    ProductCardsData data = apiResponse.Data!;
                    List<ChannelProductCard> cards = data.Cards.ToList();

                    bool apiReportsMore = data.NextCursor != null;

                    // Cooperative cancellation fix: an operator may have cancelled the job while this
                    // page's HTTP call was in flight. Re-reading status from the DB (instead of trusting
                    // jobRun.Status captured before the loop started) is what makes the stop take effect
                    // after the current page rather than only once the whole cursor is exhausted.
                    bool cancelled = await _jobRunRepository.GetStatusAsync(jobRun.Id, cancellationToken) == JobStatus.Cancelled;

                    // Forces the current page to be treated as terminal when cancelled, regardless of
                    // whether WB's cursor still reports more data РІР‚вЂќ the loop must stop after this page.
                    bool isTerminalPage = !apiReportsMore || cancelled;

                    // A page carrying no cards but advertising a cursor is meaningless for the
                    // import; advance without publishing anything (unless cancellation forces a stop).
                    if (cards.Count == 0 && apiReportsMore && !cancelled)
                    {
                        _logger.LogInformation(
                            "JobRun {JobRunId}: page {Batch} has no cards but a next cursor, advancing",
                            jobRun.Id,
                            batchNumber);

                        // Nothing is published, so the checkpoint may move past this page safely:
                        // the buffered batch keeps its own cursor and stays refetchable.
                        await _jobCheckpointRepository.UpsertAsync(
                            BuildCheckpoint(jobRun.Id, data.NextCursor, _timeProvider.GetUtcNow()), cancellationToken);

                        cursor = data.NextCursor;
                        continue;
                    }

                    if (cards.Count == 0)
                    {
                        // Empty/forced terminal page: a signal, not a batch of its own. The page held in
                        // the buffer was the real last one and is published as such; if nothing was ever
                        // buffered the job genuinely has no (more) records, and a single empty terminal
                        // batch is still published so the aggregator can finalise it.
                        List<ChannelProductCard> terminalCards =
                            bufferedBatch != null ? bufferedBatch.Cards : new List<ChannelProductCard>();

                        batchNumber++;

                        _logger.LogInformation(
                            "JobRun {JobRunId}: publishing terminal batch {Batch} with {Count} cards (cancelled={Cancelled})",
                            jobRun.Id,
                            batchNumber,
                            terminalCards.Count,
                            cancelled);

                        await PublishBatchAsync(
                            jobRun,
                            connection,
                            terminalCards,
                            isLastBatch: true,
                            batchNumber,
                            totalBatches: batchNumber,
                            cancellationToken);

                        break;
                    }

                    // Non-terminal non-empty page: the buffered page is now known not to be terminal
                    // (or cancellation has forced an early stop), so it can be published as a regular
                    // batch, and the current page takes its place in the buffer РІР‚вЂќ or is published
                    // itself as the terminal batch when this is the stop point.
                    if (bufferedBatch != null)
                    {
                        batchNumber++;

                        await PublishBatchAsync(
                            jobRun,
                            connection,
                            bufferedBatch.Cards,
                            isLastBatch: false,
                            batchNumber,
                            totalBatches: 0,
                            cancellationToken);

                        // Checkpoint the cursor of the page just published РІР‚вЂќ not of the page just
                        // fetched. A redelivery then refetches the page still sitting in the buffer,
                        // which at worst duplicates one batch that batch idempotency absorbs, instead
                        // of skipping it outright.
                        await _jobCheckpointRepository.UpsertAsync(
                            BuildCheckpoint(jobRun.Id, bufferedBatch.NextCursor, _timeProvider.GetUtcNow()), cancellationToken);
                    }

                    if (isTerminalPage)
                    {
                        batchNumber++;

                        _logger.LogInformation(
                            "JobRun {JobRunId}: publishing terminal batch {Batch} with {Count} cards (cancelled={Cancelled})",
                            jobRun.Id,
                            batchNumber,
                            cards.Count,
                            cancelled);

                        await PublishBatchAsync(
                            jobRun,
                            connection,
                            cards,
                            isLastBatch: true,
                            batchNumber,
                            totalBatches: batchNumber,
                            cancellationToken);

                        break;
                    }

                    bufferedBatch = new PendingBatch
                    {
                        Cards = cards,
                        NextCursor = data.NextCursor,
                    };

                    cursor = data.NextCursor;
                }

                _logger.LogInformation(
                    "JobRun {JobRunId}: all {Batches} batches published successfully",
                    jobRun.Id,
                    batchNumber);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("JobRun {JobRunId}: processing was cancelled", jobRun.Id);

                // Do not change status - let MassTransit redeliver. Checkpoint is already persisted.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "JobRun {JobRunId}: unexpected error during import", jobRun.Id);
                await FailJobAsync(jobRun, ex.Message, cancellationToken);
            }
        }
    }

    private async Task PublishBatchAsync(
        JobRun jobRun,
        Connection connection,
        List<ChannelProductCard> cards,
        bool isLastBatch,
        int batchNumber,
        int totalBatches,
        CancellationToken cancellationToken)
    {
        ProductBatchImportRequestedEvent batchEvent = new ProductBatchImportRequestedEvent
        {
            JobRunId = jobRun.Id,
            ConnectionId = connection.Id,
            ChannelId = connection.ChannelId,
            Cards = cards,
            IsLastBatch = isLastBatch,
            TotalBatches = totalBatches,
        };

        await _eventPublisher.PublishAsync(batchEvent, cancellationToken);

        _logger.LogInformation(
            "JobRun {JobRunId}: batch {Batch} published with {Count} cards, IsLastBatch={IsLastBatch}",
            jobRun.Id,
            batchNumber,
            cards.Count,
            isLastBatch);
    }

    private async Task FailJobAsync(JobRun jobRun, string errorMessage, CancellationToken cancellationToken)
    {
        jobRun.Status = JobStatus.Failed;
        jobRun.Error = errorMessage;
        jobRun.CompletedAt = _timeProvider.GetUtcNow();
        await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);

        try
        {
            ProductsImportCompletedEvent completedEvent = new ProductsImportCompletedEvent
            {
                JobRunId = jobRun.Id,
                IsSuccess = false,
                ErrorMessage = errorMessage,
                TotalRecordsProcessed = jobRun.RecordsCreated + jobRun.RecordsUpdated + jobRun.RecordsSkipped + jobRun.RecordsFailed,
                TotalRecordsCreated = jobRun.RecordsCreated,
                TotalRecordsUpdated = jobRun.RecordsUpdated,
                TotalRecordsSkipped = jobRun.RecordsSkipped,
                TotalRecordsFailed = jobRun.RecordsFailed,
            };

            await _eventPublisher.PublishAsync(completedEvent, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "JobRun {JobRunId}: failed to publish ProductsImportCompletedEvent after job failure",
                jobRun.Id);
        }
    }

    private static JobCheckpoint BuildCheckpoint(Guid jobRunId, ProductCardsCursor? cursor, DateTimeOffset now)
    {
        return new JobCheckpoint
        {
            JobRunId = jobRunId,
            Payload = SerializeCursor(cursor) ?? string.Empty,
            UpdatedAt = now,
        };
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
        public List<ChannelProductCard> Cards { get; set; } = new();

        public ProductCardsCursor? NextCursor { get; set; }
    }
}
