using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence;

/// <summary>
/// EF Core implementation of <see cref="INumberingSequenceStore"/>. Owns everything the allocator
/// must not know about: the tracked scheme graph, the <c>FOR UPDATE</c> statement and the reload
/// that makes a counter value authoritative once its row is locked.
/// </summary>
public class NumberingSequenceStore : INumberingSequenceStore
{
    private readonly CatalogDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="NumberingSequenceStore"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    public NumberingSequenceStore(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<NumberingScheme?> GetSchemeAsync(Guid schemeId, CancellationToken cancellationToken)
    {
        NumberingScheme? scheme = await _context.NumberingSchemes
            .Include(s => s.Segments)
                .ThenInclude(segment => segment.NumberingSequence)
            .FirstOrDefaultAsync(s => s.Id == schemeId, cancellationToken);

        if (scheme is null)
        {
            return null;
        }

        OrderSegments(scheme);

        return scheme;
    }

    /// <inheritdoc/>
    public async Task LockSequencesAsync(
        IReadOnlyCollection<Guid> sequenceIds,
        CancellationToken cancellationToken)
    {
        Guid[] orderedIds = sequenceIds.Distinct().OrderBy(id => id).ToArray();

        if (orderedIds.Length == 0)
        {
            return;
        }

        if (_context.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Numbering sequences can only be locked inside an ambient transaction. " +
                "Under autocommit PostgreSQL releases the row lock as soon as the statement returns.");
        }

        // Locking in ascending Id order keeps concurrent allocations from deadlocking each other
        // when one scheme uses several sequences.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT \"Id\" FROM catalog.\"NumberingSequences\" WHERE \"Id\" = ANY({orderedIds}) ORDER BY \"Id\" FOR UPDATE",
            cancellationToken);
    }

    /// <inheritdoc/>
    public async Task RefreshLockedSequencesAsync(
        IReadOnlyCollection<Guid> sequenceIds,
        CancellationToken cancellationToken)
    {
        foreach (Guid sequenceId in sequenceIds.Distinct().OrderBy(id => id))
        {
            EntityEntry<NumberingSequence> entry = _context.ChangeTracker
                .Entries<NumberingSequence>()
                .FirstOrDefault(candidate => candidate.Entity.Id == sequenceId)
                ?? throw new InvalidOperationException(
                    $"Numbering sequence '{sequenceId}' is not tracked by this context and cannot be locked.");

            if (entry.State == EntityState.Added)
            {
                // Created in this same transaction: the row is not visible to anyone else yet, so
                // the tracked value is authoritative and there is nothing to read back.
                continue;
            }

            await entry.ReloadAsync(cancellationToken);

            if (entry.State == EntityState.Detached)
            {
                throw new InvalidOperationException(
                    $"Numbering sequence '{sequenceId}' does not exist in the database.");
            }
        }
    }

    private static void OrderSegments(NumberingScheme scheme)
    {
        List<NumberingSegment> ordered = scheme.Segments
            .OrderBy(segment => segment.Order)
            .ToList();

        scheme.Segments.Clear();

        foreach (NumberingSegment segment in ordered)
        {
            scheme.Segments.Add(segment);
        }
    }
}
