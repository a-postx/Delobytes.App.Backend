using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Numbering;

/// <summary>
/// Persistence port of number allocation. The implementation owns the row locking, so the
/// allocator only composes values and never touches the database directly.
/// </summary>
public interface INumberingSequenceStore
{
    /// <summary>
    /// Loads the scheme with its segments ordered by <see cref="NumberingSegment.Order"/> and each
    /// segment's sequence, all tracked. The counter values are not authoritative until the rows
    /// have been locked and refreshed.
    /// </summary>
    public Task<NumberingScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken);

    /// <summary>
    /// Takes a row lock on every sequence in the given set, in a deterministic order, so concurrent
    /// allocations serialize. Throws when no transaction is ambient: under autocommit the lock
    /// would be released as soon as the statement returned.
    /// </summary>
    public Task LockSequencesAsync(IReadOnlyCollection<Guid> sequenceIds, CancellationToken cancellationToken);

    /// <summary>
    /// Reloads the tracked sequences from the database after their locks are held, replacing counter
    /// values read before the lock. Throws when a requested sequence is not tracked or not stored.
    /// </summary>
    public Task RefreshLockedSequencesAsync(IReadOnlyCollection<Guid> sequenceIds, CancellationToken cancellationToken);
}
