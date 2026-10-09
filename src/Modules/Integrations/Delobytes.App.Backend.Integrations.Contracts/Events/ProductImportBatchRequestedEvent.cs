using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations.ProcessProductsImportConsumer for each batch of product cards.
/// Consumed by Catalog.ImportProductBatchConsumer to upsert products and channel products.
/// </summary>
public record ProductImportBatchRequestedEvent
{
    /// <summary>
    /// Identifier of the SyncJob that tracks this import.
    /// </summary>
    public Guid SyncJobId { get; init; }

    /// <summary>
    /// Identifier of the Connection from which products are imported.
    /// </summary>
    public Guid ConnectionId { get; init; }

    /// <summary>
    /// Identifier of the Channel in Catalog module.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Batch of product card snapshots from the external marketplace.
    /// </summary>
    public List<WildberriesCardSnapshot> Cards { get; init; } = new();

    /// <summary>
    /// True if this is the last batch in the current import job.
    /// </summary>
    public bool IsLastBatch { get; init; }

    /// <summary>
    /// Total number of batches published for this import job. Only meaningful when
    /// <see cref="IsLastBatch"/> is true — the publisher (ProcessProductsImportConsumer) only
    /// knows the final count once it has produced the terminal batch. Carried through to
    /// <see cref="ProductImportBatchCompletedEvent.TotalBatches"/> so the aggregator can tell
    /// how many batch results it must wait for, instead of finalising as soon as any batch
    /// happens to report IsLastBatch=true regardless of whether larger sibling batches are
    /// still being processed.
    /// </summary>
    public int TotalBatches { get; init; }
}
