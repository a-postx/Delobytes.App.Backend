using Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Sales.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Sales.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IChannelProductRefRepository"/>.
/// </summary>
public class ChannelProductRefRepository : IChannelProductRefRepository
{
    private readonly SalesDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelProductRefRepository"/> class.
    /// </summary>
    /// <param name="context">Sales database context.</param>
    public ChannelProductRefRepository(SalesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<ChannelProductRef?> FindByChannelProductIdAsync(
        Guid channelProductId,
        CancellationToken cancellationToken)
    {
        return _context.ChannelProductRefs
            .FirstOrDefaultAsync(r => r.ChannelProductId == channelProductId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ChannelProductRef?> FindStaleAsync(
        Guid channelId,
        string externalProductId,
        Guid newChannelProductId,
        CancellationToken cancellationToken)
    {
        return _context.ChannelProductRefs
            .FirstOrDefaultAsync(
                r => r.ChannelId == channelId
                    && r.ExternalProductId == externalProductId
                    && r.ChannelProductId != newChannelProductId,
                cancellationToken);
    }

    /// <inheritdoc/>
    public void Add(ChannelProductRef projection)
    {
        _context.ChannelProductRefs.Add(projection);
    }

    /// <inheritdoc/>
    public void Remove(ChannelProductRef projection)
    {
        _context.ChannelProductRefs.Remove(projection);
    }

    /// <inheritdoc/>
    public Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
