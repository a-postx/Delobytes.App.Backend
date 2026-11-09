namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations once a products import run has finished asking Catalog to reconcile
/// <c>ChannelProduct.IsActive</c> against the listing that run walked.
/// </summary>
/// <remarks>
/// This event exists because no single batch can make that claim. Batches of one run are processed
/// concurrently and each holds its own transaction until commit, so the batch that happens
/// to be flagged <c>IsLastBatch</c> reaches its own reconciliation before its siblings'
/// rows are even visible — reading them as "dropped by the channel" and deactivating live links.
/// </remarks>
public record ChannelProductReconciliationRequestedEvent
{
    /// <summary>
    /// Identifier of the JobRun whose listing the reconciliation describes. Diagnostic only: the
    /// sweep is driven by <see cref="ChannelId"/> and <see cref="RunStartedAt"/>.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Identifier of the Connection the run imported through. Diagnostic only.
    /// </summary>
    public Guid ConnectionId { get; init; }

    /// <summary>
    /// The channel whose links are reconciled. A listing import describes exactly one channel.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Start instant of the run, taken from <c>JobRun.StartedAt</c>. Every card the run imported
    /// stamped <c>ChannelProduct.LastSyncedAt</c> at or after this instant, so a link stamped
    /// earlier belongs to an older run and was absent from the listing.
    /// </summary>
    /// <remarks>
    /// Required. A default value disables the sweep in the consumer rather than being read as
    /// "the beginning of time", which would deactivate every link of the channel.
    /// </remarks>
    public DateTimeOffset RunStartedAt { get; init; }
}
