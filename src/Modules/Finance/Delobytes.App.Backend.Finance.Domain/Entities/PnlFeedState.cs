using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Finance.Domain.Entities;

/// <summary>
/// Last delivery the channel feed made into the journal, per channel.
/// </summary>
/// <remarks>
/// <para>
/// This is a checkpoint of the feed, not a derivation from the journal. The journal is append-only
/// and drops duplicates before insert, so a repeated report leaves no trace there and a maximum of
/// <see cref="PnlEntry.ImportedAt"/> would not move; an empty report publishes no batch at all. A
/// dashboard that must say "these figures were refreshed at" needs the delivery instant, and only
/// the delivery itself can record it.
/// </para>
/// <para>
/// One row per channel, rewritten by every received batch. It records what the batch did, so a
/// no-op re-sync is visible as a fresh checkpoint with <see cref="LastBatchAppended"/> zero rather
/// than as a stale one.
/// </para>
/// </remarks>
public class PnlFeedState : ITenantScoped
{
    /// <summary>
    /// Gets or sets the checkpoint unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the channel the batch was delivered for.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the run that delivered the batch.
    /// </summary>
    public Guid LastJobRunId { get; set; }

    /// <summary>
    /// Gets or sets the instant the last batch finished being processed.
    /// </summary>
    public DateTimeOffset LastBatchAt { get; set; }

    /// <summary>
    /// Gets or sets the instant the last batch actually changed the data, or null when no batch of
    /// this channel has ever appended or superseded an entry.
    /// </summary>
    public DateTimeOffset? LastChangeAt { get; set; }

    /// <summary>
    /// Gets or sets the number of entries the last batch appended.
    /// </summary>
    public int LastBatchAppended { get; set; }

    /// <summary>
    /// Gets or sets the number of entries the last batch skipped as duplicates: equal to the batch
    /// size on a redelivered report, which is why it is kept next to <see cref="LastBatchAt"/>.
    /// </summary>
    public int LastBatchDuplicates { get; set; }

    /// <summary>
    /// Gets or sets the number of entries of the last batch that failed to map or to recompute.
    /// </summary>
    public int LastBatchFailed { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last modification timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }
}
