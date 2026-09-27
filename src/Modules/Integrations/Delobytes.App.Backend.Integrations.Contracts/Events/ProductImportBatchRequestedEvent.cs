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
}
