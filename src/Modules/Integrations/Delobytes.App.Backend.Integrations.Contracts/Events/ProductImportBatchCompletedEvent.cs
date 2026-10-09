namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Catalog.ImportProductBatchConsumer after processing a batch of product cards.
/// Consumed by Integrations.ProcessProductsImportConsumer to update progress counters.
/// </summary>
public record ProductImportBatchCompletedEvent
{
    /// <summary>
    /// Identifier of the SyncJob that tracks this import.
    /// </summary>
    public Guid SyncJobId { get; init; }

    /// <summary>
    /// Number of product cards processed in this batch.
    /// </summary>
    public int RecordsProcessed { get; init; }

    /// <summary>
    /// Number of products created.
    /// </summary>
    public int RecordsCreated { get; init; }

    /// <summary>
    /// Number of products updated.
    /// </summary>
    public int RecordsUpdated { get; init; }

    /// <summary>
    /// Number of products skipped (no changes required).
    /// </summary>
    public int RecordsSkipped { get; init; }

    /// <summary>
    /// Number of products that failed to import due to errors.
    /// </summary>
    public int RecordsFailed { get; init; }

    /// <summary>
    /// Error message if batch processing partially failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// True if this is the last batch in the import job.
    /// When true, it carries <see cref="TotalBatches"/> — the aggregator finalises the SyncJob
    /// once it has recorded that many batch results in total, which may happen on this event
    /// or on a later-arriving one (see ProcessProductImportBatchCompletedConsumer).
    /// </summary>
    public bool IsLastBatch { get; init; }

    /// <summary>
    /// Total number of batches expected for this import job. Propagated unchanged from
    /// <see cref="ProductImportBatchRequestedEvent.TotalBatches"/>; 0/unused unless
    /// <see cref="IsLastBatch"/> is true.
    /// </summary>
    public int TotalBatches { get; init; }
}
