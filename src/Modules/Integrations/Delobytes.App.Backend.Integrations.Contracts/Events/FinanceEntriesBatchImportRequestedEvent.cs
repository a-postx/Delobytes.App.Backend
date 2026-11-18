using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations for each batch of finance entries fetched from a channel.
/// Consumed by Finance.FinanceEntriesBatchImporter to append them to the journal.
/// </summary>
public record FinanceEntriesBatchImportRequestedEvent
{
    /// <summary>
    /// Gets the identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Gets the identifier of the Connection the entries were fetched from.
    /// </summary>
    public Guid ConnectionId { get; init; }

    /// <summary>
    /// Gets the identifier of the Channel in Catalog/Finance.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets the identifier of this batch, propagated back in the completed event.
    /// </summary>
    public Guid BatchId { get; init; }

    /// <summary>
    /// Gets the batch of finance entries.
    /// </summary>
    public List<ChannelFinanceEntrySnapshot> Entries { get; init; } = new();
}
