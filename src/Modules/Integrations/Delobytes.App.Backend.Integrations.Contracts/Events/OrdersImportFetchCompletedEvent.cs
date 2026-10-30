namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations.ProcessOrdersImportConsumer after it has finished fetching a run and
/// persisted the final checkpoint. Replaces the empty terminal
/// <see cref="OrdersBatchImportRequestedEvent"/> as the signal that tells
/// Integrations.ProcessOrdersBatchImportCompletedConsumer (aggregator) how many batch results to
/// expect, without routing that signal through the Sales upsert pipeline.
/// </summary>
public record OrdersImportFetchCompletedEvent
{
    /// <summary>
    /// Identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Identifier of the Connection from which orders were imported.
    /// </summary>
    public Guid ConnectionId { get; init; }

    /// <summary>
    /// Number of batch results the aggregator must record before finalising the run.
    /// </summary>
    public int TotalBatches { get; init; }

    /// <summary>
    /// Newest source change instant the run observed, or null when the run fetched nothing or is
    /// a backfill run.
    /// </summary>
    public DateTimeOffset? MaxChangedAt { get; init; }

    /// <summary>
    /// Handover watermark captured before the final backfill catch-up request, or null when the
    /// run is not a completed backfill.
    /// </summary>
    public DateTimeOffset? HandoverWatermark { get; init; }

    /// <summary>
    /// Whether the run walked the backfill days rather than applying the delta watermark.
    /// </summary>
    public bool IsBackfill { get; init; }

    /// <summary>
    /// Whether this run completed backfill catch-up.
    /// </summary>
    public bool IsBackfillCompleted { get; init; }

    /// <summary>
    /// Number of source requests issued, including throttled attempts.
    /// </summary>
    public int PagesFetched { get; init; }

    /// <summary>
    /// End of the run window that was fetched.
    /// </summary>
    public DateTimeOffset WindowTo { get; init; }
}
