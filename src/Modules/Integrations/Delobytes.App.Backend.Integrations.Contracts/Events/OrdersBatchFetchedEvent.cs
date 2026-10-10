using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations for each fetched batch of channel orders.
/// Consumed by Sales to upsert orders, lines and settlements.
/// </summary>
public record OrdersBatchFetchedEvent
{
    /// <summary>
    /// Identifier of the SyncJob that tracks this fetch.
    /// </summary>
    public Guid SyncJobId { get; init; }

    /// <summary>
    /// Identifier of the Connection from which orders were fetched.
    /// </summary>
    public Guid ConnectionId { get; init; }

    /// <summary>
    /// Identifier of the Channel in Catalog module.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Code of the source channel, carried so the consumer can pick the right mapper without
    /// re-resolving the connection or re-deriving the code from the template.
    /// </summary>
    public string ChannelCode { get; init; } = default!;

    /// <summary>
    /// Batch of order snapshots from the external marketplace.
    /// </summary>
    public List<ChannelOrderSnapshot> Orders { get; init; } = new();

    /// <summary>
    /// True if this is the last batch in the current fetch job.
    /// </summary>
    public bool IsLastBatch { get; init; }
}
