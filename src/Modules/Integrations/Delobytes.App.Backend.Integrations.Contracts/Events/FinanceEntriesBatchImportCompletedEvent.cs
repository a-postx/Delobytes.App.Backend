namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Finance after a batch of finance entries has been processed. Consumed by
/// Integrations to aggregate the run; the watermark advances only once every batch of a window is
/// confirmed.
/// </summary>
public record FinanceEntriesBatchImportCompletedEvent
{
    /// <summary>
    /// Gets the identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Gets the identifier of the batch this result corresponds to.
    /// </summary>
    public Guid BatchId { get; init; }

    /// <summary>
    /// Gets the number of entries appended.
    /// </summary>
    public int Appended { get; init; }

    /// <summary>
    /// Gets the number of entries skipped as duplicates.
    /// </summary>
    public int Duplicates { get; init; }

    /// <summary>
    /// Gets the number of existing entries replaced by a newer report instance.
    /// </summary>
    public int Superseded { get; init; }

    /// <summary>
    /// Gets the number of entries rejected as invalid.
    /// </summary>
    public int Rejected { get; init; }

    /// <summary>
    /// Gets the number of entries that failed to be mapped or processed.
    /// </summary>
    public int Failed { get; init; }

    /// <summary>
    /// Gets the number of appended entries whose attribution key matched no order line yet.
    /// </summary>
    public int Unattributed { get; init; }

    /// <summary>
    /// Gets the number of rows of a new report instance that matched no current entry, which is the
    /// signal that row-level supersede stopped working.
    /// </summary>
    public int UnmatchedInstanceRows { get; init; }
}
