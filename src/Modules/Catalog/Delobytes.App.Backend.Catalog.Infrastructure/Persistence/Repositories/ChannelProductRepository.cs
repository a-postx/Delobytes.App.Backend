using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IChannelProductRepository"/>.
/// </summary>
public class ChannelProductRepository : IChannelProductRepository
{
    private readonly CatalogDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelProductRepository"/> class.
    /// </summary>
    /// <param name="context">Catalog database context.</param>
    public ChannelProductRepository(CatalogDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<ChannelProduct>> GetAbandonedLinksAsync(
        Guid channelId,
        DateTimeOffset syncedBefore,
        CancellationToken cancellationToken)
    {
        // Links the run never touched keep a stamp from an earlier run, or none at all when they were
        // created outside any import. Only the former is evidence of absence: a link with no stamp
        // has never been seen by an import, so no run can claim to have missed it.
        return await _context.ChannelProducts
            .Where(cp => cp.ChannelId == channelId
                && cp.IsActive
                && cp.LastSyncedAt != null
                && cp.LastSyncedAt < syncedBefore)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
