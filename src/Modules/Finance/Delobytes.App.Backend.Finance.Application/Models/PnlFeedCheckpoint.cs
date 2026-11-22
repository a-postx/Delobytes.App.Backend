namespace Delobytes.App.Backend.Finance.Application.Models;

/// <summary>
/// Outcome of one received batch, as recorded in the per-channel feed checkpoint.
/// </summary>
/// <param name="ChannelId">Channel the batch was delivered for.</param>
/// <param name="JobRunId">Run that delivered the batch.</param>
/// <param name="BatchCompletedAt">Instant the batch finished being processed.</param>
/// <param name="Appended">Entries the batch appended to the journal.</param>
/// <param name="Duplicates">Entries skipped because their idempotency key was already present.</param>
/// <param name="Failed">Entries that failed to map, plus lines the recompute failed for.</param>
public sealed record PnlFeedCheckpoint(
    Guid ChannelId,
    Guid JobRunId,
    DateTimeOffset BatchCompletedAt,
    int Appended,
    int Duplicates,
    int Failed);
