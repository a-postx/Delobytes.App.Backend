using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Finalises orders import runs: hands each delivery to <see cref="OrdersBatchAggregator"/> and
/// publishes the completion event when the aggregator reports the run finalised. Idempotent through
/// the batch's deterministic BatchId and JobRun's xmin concurrency token, both enforced by the
/// aggregator.
/// </summary>
public class OrdersImportFinisher
{
    private readonly OrdersBatchAggregator _aggregator;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<OrdersImportFinisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersImportFinisher"/> class.
    /// </summary>
    /// <param name="aggregator">Batch aggregator that decides when the run is finalisable.</param>
    /// <param name="eventPublisher">Event publisher.</param>
    /// <param name="logger">Logger instance.</param>
    public OrdersImportFinisher(
        OrdersBatchAggregator aggregator,
        IEventPublisher eventPublisher,
        ILogger<OrdersImportFinisher> logger)
    {
        _aggregator = aggregator;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    /// <summary>
    /// Processes the OrdersBatchImportCompletedEvent message.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="batchId">
    /// The batch's deterministic identifier, propagated unchanged from
    /// <see cref="OrdersBatchImportRequestedEvent.BatchId"/>, so a redelivered batch is recognised as
    /// already recorded instead of being counted twice.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessAsync(
        OrdersBatchImportCompletedEvent message,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OrdersImportFinisher: JobRunId={JobRunId}, BatchId={BatchId}",
            message.JobRunId,
            batchId);

        OrdersImportCompletion? completion = await _aggregator.RecordBatchResultAsync(
            message.JobRunId, message, batchId, cancellationToken);

        await PublishIfOwedAsync(completion, cancellationToken);
    }

    /// <summary>
    /// Processes the OrdersImportFetchCompletedEvent message, published by the orchestrator once it
    /// has finished fetching a run and persisted the final checkpoint.
    /// </summary>
    /// <param name="message">Event message.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ProcessFetchCompletedAsync(
        OrdersImportFetchCompletedEvent message,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "OrdersImportFinisher: fetch completed for JobRunId={JobRunId}, TotalBatches={TotalBatches}",
            message.JobRunId,
            message.TotalBatches);

        OrdersImportCompletion? completion = await _aggregator.RecordExpectedBatchCountAsync(
            message.JobRunId, message.TotalBatches, cancellationToken);

        await PublishIfOwedAsync(completion, cancellationToken);
    }

    private async Task PublishIfOwedAsync(OrdersImportCompletion? completion, CancellationToken cancellationToken)
    {
        if (completion == null || !completion.ShouldPublishCompletedEvent)
        {
            return;
        }

        OrdersImportCompletedEvent completedEvent = new OrdersImportCompletedEvent
        {
            JobRunId = completion.JobRun.Id,
            TotalCreated = completion.JobRun.RecordsCreated,
            TotalUpdated = completion.JobRun.RecordsUpdated,
            Status = completion.JobRun.Status
        };

        await _eventPublisher.PublishAsync(completedEvent, cancellationToken);

        _logger.LogInformation(
            "Published OrdersImportCompletedEvent for JobRun {JobRunId}",
            completion.JobRun.Id);
    }
}
