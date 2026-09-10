using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ChannelRepository : IChannelRepository
{
    private readonly CatalogDbContext _context;

    public ChannelRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<Channel?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Channels.FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public void Add(Channel channel)
    {
        _context.Channels.Add(channel);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
