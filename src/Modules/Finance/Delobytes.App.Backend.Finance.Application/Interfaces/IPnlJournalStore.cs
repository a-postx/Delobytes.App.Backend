using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Persistence port of the P&amp;L journal: the lookups the append needs before writing and the two
/// writes it performs. Implemented with set-based EF queries; the append itself is a batch insert.
/// </summary>
public interface IPnlJournalStore
{
    /// <summary>
    /// Loads the current entries whose idempotency key is one of <paramref name="keys"/>.
    /// </summary>
    /// <param name="keys">Idempotency keys to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The entries already present.</returns>
    public Task<IReadOnlyList<PnlEntry>> LoadByIdentityAsync(
        IReadOnlyCollection<JournalIdentityKey> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the current entries matching <paramref name="keys"/>, whichever report instance wrote
    /// them.
    /// </summary>
    /// <param name="keys">Instance-independent identity keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current entries matching the keys.</returns>
    public Task<IReadOnlyList<PnlEntry>> LoadCurrentByComparisonAsync(
        IReadOnlyCollection<JournalComparisonKey> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the current entries that can be attributed to the order lines behind
    /// <paramref name="keys"/>.
    /// </summary>
    /// <param name="keys">Attribution keys of the order lines.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The current entries matching the keys.</returns>
    public Task<IReadOnlyList<PnlEntry>> LoadCurrentByAttributionAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns the distinct report instance keys already recorded for one report.
    /// </summary>
    /// <param name="channelId">Channel identifier.</param>
    /// <param name="source">Journal source.</param>
    /// <param name="reportId">Report identity (the part before the colon of the instance key).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The instance keys already present.</returns>
    public Task<IReadOnlyCollection<string>> LoadInstanceKeysAsync(
        Guid channelId,
        PnlSource source,
        string reportId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Appends entries in one batch.
    /// </summary>
    /// <param name="entries">Entries to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task AppendAsync(IReadOnlyCollection<PnlEntry> entries, CancellationToken cancellationToken);

    /// <summary>
    /// Marks rows as superseded in one set-based update.
    /// </summary>
    /// <param name="instructions">Supersede instructions.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SupersedeAsync(
        IReadOnlyCollection<SupersedeInstruction> instructions,
        CancellationToken cancellationToken);
}
