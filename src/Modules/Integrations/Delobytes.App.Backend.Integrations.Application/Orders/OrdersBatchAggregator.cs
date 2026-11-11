using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Jobs;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Orders-import adapter over <see cref="ImportRunCompletion"/>: it knows only what is specific to
/// this path — that batch identity and idempotency key are the deterministic BatchId, that the
/// expected batch count may also be read from the run's fetch checkpoint, and that a finalised
/// successful run earns its sync watermark.
/// </summary>
public sealed class OrdersBatchAggregator
{
    private readonly IJobCheckpointRepository _jobCheckpointRepository;
    private readonly ImportRunCompletion _completion;
    private readonly OrdersImportWatermarkWriter _watermarkWriter;
    private readonly ILogger<OrdersBatchAggregator> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersBatchAggregator"/> class.
    /// </summary>
    /// <param name="jobCheckpointRepository">Checkpoint repository, holds the fetch outcome.</param>
    /// <param name="completion">Shared batch-completion rules.</param>
    /// <param name="watermarkWriter">Writer that persists the synced position of a successful run.</param>
    /// <param name="logger">Logger instance.</param>
    public OrdersBatchAggregator(
        IJobCheckpointRepository jobCheckpointRepository,
        ImportRunCompletion completion,
        OrdersImportWatermarkWriter watermarkWriter,
        ILogger<OrdersBatchAggregator> logger)
    {
        _jobCheckpointRepository = jobCheckpointRepository;
        _completion = completion;
        _watermarkWriter = watermarkWriter;
        _logger = logger;
    }

    /// <summary>
    /// Records one batch result under the batch's deterministic id, then finalises the run if every
    /// expected result is recorded.
    /// </summary>
    /// <param name="jobRunId">Run the batch belongs to.</param>
    /// <param name="message">Batch result as delivered by the transport.</param>
    /// <param name="batchId">Deterministic batch identifier used as the idempotency key.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run and whether its completion event is owed, or <c>null</c> when the run is gone.</returns>
    public async Task<ImportRunResult?> RecordBatchResultAsync(
        Guid jobRunId,
        OrdersBatchImportCompletedEvent message,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        // The checkpoint is the durable record the orchestrator writes before it announces
        // anything, so a batch result delivered ahead of the fetch-completed event reads the same
        // threshold the event carries.
        OrdersImportCheckpointPayload? fetchedPayload = await LoadFetchedPayloadAsync(jobRunId, cancellationToken);

        ImportBatchDelivery delivery = new ImportBatchDelivery(
            JobRunId: jobRunId,
            MessageId: batchId,
            RecordsProcessed: message.Created + message.Updated + message.Skipped + message.Failed,
            RecordsCreated: message.Created,
            RecordsUpdated: message.Updated,
            RecordsSkipped: message.Skipped,
            RecordsFailed: message.Failed,
            ErrorMessage: null,
            IsLastBatch: false,
            ExpectedBatchCount: null,

            // An in-progress checkpoint carries a running batch total, not the final count, so only
            // a finished fetch may supply the threshold. Its presence still proves the run's outcome
            // is readable, which is what finalisation needs.
            FallbackBatchCount: fetchedPayload?.Phase == OrdersImportCheckpointPhase.FetchCompleted
                ? fetchedPayload.TotalBatches
                : null,
            CanFinalize: fetchedPayload != null,
            RepublishesCompletionForRedelivery: true);

        ImportRunResult? result = await _completion.RecordBatchAsync(delivery, cancellationToken);

        if (result == null)
        {
            return null;
        }

        if (result.IsFinalized)
        {
            await WriteWatermarkAsync(result.JobRun, fetchedPayload, cancellationToken);
        }

        return result;
    }

    /// <summary>
    /// Records the batch count announced by the completion event, then finalises the run if that was
    /// the only missing piece.
    /// </summary>
    /// <param name="jobRunId">Run the event belongs to.</param>
    /// <param name="totalBatches">Batch count announced by the completion event.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The run and whether its completion event is owed, or <c>null</c> when the run is gone.</returns>
    public async Task<ImportRunResult?> RecordExpectedBatchCountAsync(
        Guid jobRunId,
        int totalBatches,
        CancellationToken cancellationToken)
    {
        OrdersImportCheckpointPayload? fetchedPayload = await LoadFetchedPayloadAsync(jobRunId, cancellationToken);

        ImportRunResult? result = await _completion.AnnounceExpectedBatchCountAsync(
            jobRunId,
            totalBatches,
            fetchedPayload != null,
            cancellationToken);

        if (result != null && result.IsFinalized)
        {
            await WriteWatermarkAsync(result.JobRun, fetchedPayload, cancellationToken);
        }

        return result;
    }

    private async Task WriteWatermarkAsync(
        JobRun jobRun,
        OrdersImportCheckpointPayload? payload,
        CancellationToken cancellationToken)
    {
        if (payload == null)
        {
            // Without the fetch outcome there is no watermark to write; the run still finalises
            // rather than staying Running until the watchdog closes it.
            _logger.LogWarning(
                "JobRun {JobRunId} finalised without a readable fetch checkpoint, so the sync watermark was not written",
                jobRun.Id);

            return;
        }

        await _watermarkWriter.WriteAsync(jobRun, payload, cancellationToken);
    }

    /// <summary>
    /// Reads the checkpoint payload once. Any readable payload is returned: the caller decides
    /// whether its phase makes the batch count usable, while the payload itself is what the sync
    /// watermark is written from.
    /// </summary>
    private async Task<OrdersImportCheckpointPayload?> LoadFetchedPayloadAsync(
        Guid jobRunId,
        CancellationToken cancellationToken)
    {
        JobCheckpoint? checkpoint = await _jobCheckpointRepository
            .GetByJobRunIdAsync(jobRunId, cancellationToken);

        if (checkpoint == null)
        {
            return null;
        }

        return OrdersImportCheckpointStore.TryDeserializePayload(jobRunId, checkpoint.Payload);
    }
}
