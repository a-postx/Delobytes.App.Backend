using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations.ProcessOrdersBatchImportCompletedConsumer (aggregator) when the
/// entire orders import job reaches a terminal state. Consumed by Integrations to finalize the
/// JobRun; kept in Contracts (like <see cref="ProductsImportCompletedEvent"/>) in case other
/// modules need to react to import completion later.
/// </summary>
public record OrdersImportCompletedEvent
{
    /// <summary>
    /// Identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Total number of orders created across all batches.
    /// </summary>
    public int TotalCreated { get; init; }

    /// <summary>
    /// Total number of orders updated across all batches.
    /// </summary>
    public int TotalUpdated { get; init; }

    /// <summary>
    /// Final status of the job run (Success, Failed or Cancelled).
    /// </summary>
    public JobStatus Status { get; init; }
}
