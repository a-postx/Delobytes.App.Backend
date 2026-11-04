using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations.ProcessProductsImportConsumer for each batch of product cards.
/// Consumed by Catalog.ImportProductBatchConsumer to upsert products and channel products.
/// </summary>
public record ProductBatchImportRequestedEvent
{
    /// <summary>
    /// Identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

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
    /// Moment the import job started, in UTC, as recorded on its <c>JobRun.StartedAt</c>.
    /// </summary>
    /// <remarks>
    /// The full-set reconciliation that Catalog performs on the terminal batch has no way to know
    /// which cards the earlier batches of the same job carried — each batch is consumed in
    /// isolation. Every card an import touches stamps <c>ChannelProduct.LastSyncedAt</c> with the
    /// current instant, so a link whose stamp predates the job's start is one this job never saw,
    /// and that comparison is what makes the sweep possible. Null (an older publisher, or a job
    /// whose start was never recorded) disables the sweep rather than defaulting it, because a
    /// made-up boundary would deactivate every link in the tenant.
    /// </remarks>
    public DateTimeOffset? RunStartedAt { get; init; }

    /// <summary>
    /// True when the publisher stopped before the channel's listing was exhausted, so this batch
    /// is terminal for the job but does not describe the whole catalogue.
    /// </summary>
    /// <remarks>
    /// Set for a cancelled job: the producer still emits a terminal batch so the aggregator can
    /// finalise, and the cards it never fetched must not be read as "absent from the channel".
    /// Catalog's full-set reconciliation is a claim about the whole listing, so it is skipped
    /// whenever this is true.
    /// </remarks>
    public bool IsPartial { get; init; }

    /// <summary>
    /// Total number of batches published for this import job. Only meaningful when
    /// <see cref="IsLastBatch"/> is true — the publisher (ProcessProductsImportConsumer) only
    /// knows the final count once it has produced the terminal batch. Carried through to
    /// <see cref="ProductBatchImportCompletedEvent.TotalBatches"/> so the aggregator can tell
    /// how many batch results it must wait for, instead of finalising as soon as any batch
    /// happens to report IsLastBatch=true regardless of whether larger sibling batches are
    /// still being processed.
    /// </summary>
    public int TotalBatches { get; init; }
}
