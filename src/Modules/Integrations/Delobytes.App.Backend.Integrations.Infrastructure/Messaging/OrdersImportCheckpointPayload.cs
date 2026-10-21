namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging;

/// <summary>
/// JobCheckpoint payload shared by the orders orchestrator and aggregator.
/// </summary>
public sealed record OrdersImportCheckpointPayload
{
    /// <summary>
    /// Gets the number of batch results the aggregator must record before finalising the run.
    /// While <see cref="IsFetchCompleted"/> is false, this value is the number of batches published
    /// so far; once <see cref="IsFetchCompleted"/> is true, it is the final batch count.
    /// </summary>
    public int TotalBatches { get; init; }

    /// <summary>
    /// Gets a value indicating whether the orchestrator finished fetching all the work units for
    /// this run. Null (for payloads written before this change) is treated as true (completed).
    /// The aggregator finalises the run only when <see cref="IsFetchCompleted"/> != false and
    /// the recorded batch results count equals <see cref="TotalBatches"/>.
    /// </summary>
    public bool? IsFetchCompleted { get; init; }

    /// <summary>
    /// Gets the handover watermark captured before the final catch-up request.
    /// </summary>
    public DateTimeOffset? HandoverWatermark { get; init; }

    /// <summary>
    /// Gets the newest source change instant the run observed, or null when the run fetched
    /// nothing and therefore earned no watermark. Always null on a backfill run: during a
    /// backwards walk the newest day is fetched first, so publishing its instant as the delta
    /// watermark would mark the older days still to come as already imported.
    /// </summary>
    public DateTimeOffset? MaxChangedAt { get; init; }

    /// <summary>
    /// Gets the end of the run window at the time it was first fetched. Persisted so a redelivered
    /// run re-reads exactly the same window instead of sliding forward with the clock.
    /// </summary>
    public DateTimeOffset WindowTo { get; init; }

    /// <summary>
    /// Gets the number of source requests, including throttled attempts.
    /// </summary>
    public int PagesFetched { get; init; }

    /// <summary>
    /// Gets a value indicating whether the run walked the backfill days rather than applying the
    /// delta watermark. Carried in the payload because the aggregator cannot re-derive it: by the
    /// time a batch result arrives the backfill state may already have flipped to completed, and
    /// the run must be judged by the branch it actually took.
    /// </summary>
    public bool IsBackfill { get; init; }

    /// <summary>
    /// Gets a value indicating whether this run completed catch-up.
    /// </summary>
    public bool IsBackfillCompleted { get; init; }

    /// <summary>
    /// Gets the legacy observed watermark retained for payload compatibility.
    /// </summary>
    public DateTimeOffset? MaxObservedLastChangeDate { get; init; }
}
