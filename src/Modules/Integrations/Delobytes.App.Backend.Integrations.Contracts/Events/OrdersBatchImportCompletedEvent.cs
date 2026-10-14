namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Sales.OrdersBatchImportRequestedConsumer after processing a batch of orders.
/// Consumed by Integrations.ProcessOrdersBatchImportCompletedConsumer (aggregator) to update
/// progress counters and, on the terminal batch, finalize the JobRun.
/// </summary>
public record OrdersBatchImportCompletedEvent
{
    /// <summary>
    /// Identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Identifier of the batch this result corresponds to, propagated unchanged from
    /// <see cref="OrdersBatchImportRequestedEvent.BatchId"/>.
    /// </summary>
    public Guid BatchId { get; init; }

    /// <summary>
    /// Number of orders created.
    /// </summary>
    public int Created { get; init; }

    /// <summary>
    /// Number of orders updated.
    /// </summary>
    public int Updated { get; init; }

    /// <summary>
    /// Number of orders skipped (no changes required).
    /// </summary>
    public int Skipped { get; init; }

    /// <summary>
    /// Number of orders that failed to import due to errors.
    /// </summary>
    public int Failed { get; init; }
}
