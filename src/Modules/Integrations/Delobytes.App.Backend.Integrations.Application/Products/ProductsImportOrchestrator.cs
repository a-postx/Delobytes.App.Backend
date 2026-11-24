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

            // Kept across a deferral: the full-set reconciliation compares link stamps with this
            // boundary, so moving it on resume would make everything the earlier attempt imported
            // look unlisted.
            jobRun.StartedAt ??= _timeProvider.GetUtcNow();

            // The run has been dispatched, so its deferral is spent; a leftover future NotBefore
            // keeps the row out of the active-run unique index.
            jobRun.NotBefore = null;

            await _jobRunRepository.UpdateAsync(jobRun, cancellationToken);
        }

        IChannelApiClient apiClient = _clientFactory.Create(connection.SystemChannelTemplate);

        int pageSize = ResolvePageSize(connection);
        int pageNumber = 0;

        // Resume from the saved checkpoint when redelivered mid-import. The published-batch count is
        // part of that state: a resumed run that restarted its own counter would announce a lower
        // total than the run has already produced, and the aggregator would finalise it on a
        // non-terminal batch.
        JobCheckpoint? checkpoint = await _jobCheckpointRepository.GetByJobRunIdAsync(jobRun.Id, cancellationToken);
        (ProductCardsCursor? cursor, int publishedBatches) = DeserializeCheckpoint(checkpoint?.Payload);


        // Identifiers this run has already published, so a page that repeats a card cannot count it
        // twice. Kept for the whole run: a channel is free to repeat a card across pages or inside
        // one response, and neither is a card the import should process again.
        HashSet<string> publishedOfferIds = new HashSet<string>(StringComparer.Ordinal);

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

                    pageNumber++;

                    ApiResponse<ProductCardsData> apiResponse = await apiClient.GetProductCardsAsync(
                        connection.Id,
                        cursor,
                        pageSize,
                        cancellationToken);

                    if (!apiResponse.IsSuccess)
                    {
                        if (apiResponse.RetryAt is DateTimeOffset retryAt)
                        {
                            await DeferAsync(jobRun, retryAt);
                            return;
                        }

                        string errorMsg = apiResponse.ErrorMessage ?? "Unknown API error";

                        _logger.LogError(
                            "JobRun {JobRunId}: Products API call failed (page {Page}): {Error}",
                            jobRun.Id,
                            pageNumber,
                            errorMsg);

                        await FailJobAsync(jobRun, errorMsg, cancellationToken);
                        return;
                    }

                    ProductCardsData data = apiResponse.Data!;
                    List<ChannelProductCard> cards = data.Cards.ToList();

                    // Wildberries has been seen returning a card twice inside one response (and more
                    // cards than the requested limit), which published the repeated cards as their own
                    // batch and inflated the run's counters. The cause is the channel's, but the count
                    // is ours, so the page is filtered before it is published.
                    cards = DropAlreadyPublished(cards, publishedOfferIds, jobRun.Id, pageNumber);

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
                    // import; skip it without publishing anything (unless cancellation forces a stop).
                    if (cards.Count == 0 && apiReportsMore && !cancelled)
                    {
                        _logger.LogInformation(
                            "JobRun {JobRunId}: page {Page} has no cards but a next cursor, advancing",
                            jobRun.Id,
                            pageNumber);

                        // The checkpoint may only move while nothing is waiting to be published. A
                        // buffered page holds the cards already fetched but not yet sent: advancing
                        // past them loses them for good, because a redelivery resumes from the
                        // checkpoint and never refetches what it skipped. With only this page empty,
                        // the buffer's own cursor is the newer of the two anyway.
                        if (bufferedBatch == null)
                        {
                            await _jobCheckpointRepository.UpsertAsync(
                                BuildCheckpoint(jobRun.Id, data.NextCursor, publishedBatches, _timeProvider.GetUtcNow()), cancellationToken);
                        }
                        else
                        {
                            _logger.LogInformation(
                                "JobRun {JobRunId}: page {Page} has no cards but a next cursor; checkpoint " +
                                "stays at page {BufferedPage}, which a redelivery must refetch",
                                jobRun.Id,
                                pageNumber,
                                pageNumber - 1);
                        }

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

                        publishedBatches = await PublishPageAsync(
                            jobRun,
                            connection,
                            terminalCards,
                            isTerminalPage: true,
                            publishedBatches,
                            cancellationToken);

                        break;
                    }

                    // Non-terminal non-empty page: the buffered page is now known not to be terminal
                    // (or cancellation has forced an early stop), so it can be published as a regular
                    // batch, and the current page takes its place in the buffer РІР‚вЂќ or is published
                    // itself as the terminal batch when this is the stop point.
                    if (bufferedBatch != null)
                    {
                        publishedBatches = await PublishPageAsync(
                            jobRun,
                            connection,
                            bufferedBatch.Cards,
                            isTerminalPage: false,
                            publishedBatches,
                            cancellationToken);

                        // Checkpoint the cursor of the page just published, not of the page just
                        // fetched. A redelivery then refetches the page still sitting in the buffer,
                        // which at worst duplicates one batch that batch idempotency absorbs, instead
                        // of skipping it outright. All chunks of the page are published by now.
                        await _jobCheckpointRepository.UpsertAsync(
                            BuildCheckpoint(jobRun.Id, bufferedBatch.NextCursor, publishedBatches, _timeProvider.GetUtcNow()), cancellationToken);
                    }

                    if (isTerminalPage)
                    {
                        publishedBatches = await PublishPageAsync(
                            jobRun,
                            connection,
                            cards,
                            isTerminalPage: true,
                            publishedBatches,
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
                    publishedBatches);
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

    /// <summary>
    /// Removes the cards this run has already published, and records the ones it keeps.
    /// </summary>
    /// <remarks>
    /// A card is identified by its external product id, which is what the import also uses as the
    /// channel link's identity. A page that repeats an identifier is the channel contradicting
    /// itself: processing it again cannot create or update anything the first pass did not, but it
    /// does add to the run's counters and re-queues the same photos.
    /// </remarks>
    /// <param name="cards">Cards of the page, in the order the channel returned them.</param>
    /// <param name="publishedOfferIds">Identifiers published before this page; updated in place.</param>
    /// <param name="jobRunId">Run the page belongs to, for logging.</param>
    /// <param name="pageNumber">Page the cards came from, for logging.</param>
    /// <returns>The cards of the page that were not published yet, in their original order.</returns>
    private List<ChannelProductCard> DropAlreadyPublished(
        List<ChannelProductCard> cards,
        HashSet<string> publishedOfferIds,
        Guid jobRunId,
        int pageNumber)
    {
        if (cards.Count == 0)
        {
            return cards;
        }

        List<ChannelProductCard> kept = new List<ChannelProductCard>(cards.Count);
        int dropped = 0;

        foreach (ChannelProductCard card in cards)
        {
            if (publishedOfferIds.Add(card.ExternalProductId))
            {
                kept.Add(card);
            }
            else
            {
                dropped++;
            }
        }

        if (dropped > 0)
        {
            _logger.LogWarning(
                "JobRun {JobRunId}: page {Page} returned {Count} cards, {Dropped} of which repeat an " +
                "identifier already published by this run. Dropped them; {Kept} cards remain.",
                jobRunId,
                pageNumber,
                cards.Count,
                dropped,
                kept.Count);
        }

        return kept;
    }

    private int ResolvePageSize(Connection connection)
    {
        int configured = _tunings.Resolve(connection.SystemChannelTemplate.Code).PageSize;

        return configured > 0 ? configured : ProductsImportTuning.DefaultPageSize;
    }

    private int ResolveEventBatchSize(Connection connection)
    {
        int configured = _tunings.Resolve(connection.SystemChannelTemplate.Code).BatchSize;

        return configured > 0 ? configured : ProductsImportTuning.DefaultBatchSize;
    }

    /// <summary>
    /// Publishes one fetched page as one or more batch events and returns the updated count of
    /// published events.
    /// </summary>
    /// <remarks>
    /// A page and a batch are different sizes: a marketplace returns full cards per page, and the
    /// same volume would otherwise land in one message. The returned count is what the aggregator
    /// compares against its recorded batch rows, so it counts events, not pages.
    /// </remarks>
    /// <param name="jobRun">Run the page belongs to.</param>
    /// <param name="connection">Connection the run imports through.</param>
    /// <param name="cards">Cards of the page, in the order the channel returned them.</param>
    /// <param name="isTerminalPage">True for the publish that ends the run.</param>
    /// <param name="publishedBatches">Events published before this page.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of events published so far, this page included.</returns>
    private async Task<int> PublishPageAsync(
        JobRun jobRun,
        Connection connection,
        List<ChannelProductCard> cards,
        bool isTerminalPage,
        int publishedBatches,
        CancellationToken cancellationToken)
    {
        if (cards.Count == 0)
        {
            // Nothing to chunk. A terminal publish still emits one event so the aggregator can
            // finalise a run that genuinely had no records.
            int emptyTotalBatches = isTerminalPage ? publishedBatches + 1 : 0;

            await PublishBatchAsync(
                jobRun,
                connection,
                cards,
                isLastBatch: isTerminalPage,
                publishedBatches + 1,
                emptyTotalBatches,
                cancellationToken);

            return publishedBatches + 1;
        }

        int eventBatchSize = ResolveEventBatchSize(connection);
        int chunkCount = (cards.Count + eventBatchSize - 1) / eventBatchSize;

        // Only the last chunk of the terminal publish reports the run total: earlier chunks are
        // ordinary batches and carry 0, exactly as non-terminal pages do. The aggregator reads the
        // value from the batch that ends the run.
        int runTotalBatches = isTerminalPage ? publishedBatches + chunkCount : 0;

        for (int index = 0; index < chunkCount; index++)
        {
            int offset = index * eventBatchSize;
            int length = Math.Min(eventBatchSize, cards.Count - offset);
            List<ChannelProductCard> chunk = cards.GetRange(offset, length);

            publishedBatches++;

            bool isLastChunk = isTerminalPage && index == chunkCount - 1;

            await PublishBatchAsync(
                jobRun,
                connection,
                chunk,
                isLastChunk,
                publishedBatches,
                isLastChunk ? runTotalBatches : 0,
                cancellationToken);
        }

        return publishedBatches;
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

    /// <summary>
    /// Parks the run until the channel allows the next attempt instead of failing it. The checkpoint
    /// already names the last published page, so the walk resumes there; Pending is the only status
    /// the worker dispatches, and the next dispatch clears the deferral.
    /// </summary>
    private async Task DeferAsync(JobRun jobRun, DateTimeOffset retryAt)
    {
        jobRun.Status = JobStatus.Pending;
        jobRun.NotBefore = retryAt;

        // CancellationToken.None on purpose: a cancelled consume must still persist the deferral,
        // or the run stays immediately due and the worker re-publishes it on every tick.
        await _jobRunRepository.UpdateAsync(jobRun, CancellationToken.None);

        _logger.LogWarning(
            "JobRun {JobRunId}: products import deferred, the channel rate limiter allows the next " +
            "attempt at {RetryAt:O}",
            jobRun.Id,
            retryAt);
    }

    private static JobCheckpoint BuildCheckpoint(
        Guid jobRunId, ProductCardsCursor? cursor, int publishedBatches, DateTimeOffset now)
    {
        return new JobCheckpoint
        {
            JobRunId = jobRunId,
            Payload = JsonSerializer.Serialize(new CheckpointPayload
            {
                Cursor = cursor,
                PublishedBatches = publishedBatches,
            }),
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Reads the resume state. A checkpoint written before the batch counter was persisted carries
    /// the bare cursor, so it is read as the cursor of a run that has published nothing yet.
    /// </summary>
    private static (ProductCardsCursor? Cursor, int PublishedBatches) DeserializeCheckpoint(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return (null, 0);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(nameof(CheckpointPayload.Cursor), out _))
            {
                CheckpointPayload? payload = document.RootElement.Deserialize<CheckpointPayload>();

                return payload == null ? (null, 0) : (payload.Cursor, payload.PublishedBatches);
            }

            return (document.RootElement.Deserialize<ProductCardsCursor>(), 0);
        }
        catch
        {
            return (null, 0);
        }
    }

    /// <summary>
    /// Resume state of a products walk. <see cref="PublishedBatches"/> is the number of batch events
    /// the run has published across every attempt, so the terminal batch keeps announcing the run's
    /// real total after a deferral.
    /// </summary>
    private sealed class CheckpointPayload
    {
        public ProductCardsCursor? Cursor { get; init; }

        public int PublishedBatches { get; init; }
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
