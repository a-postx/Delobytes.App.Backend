using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
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

    public Task<List<Channel>> GetAllAsync(CancellationToken ct)
    {
        return _context.Channels.ToListAsync(ct);
    }

    public void Add(Channel channel)
    {
        _context.Channels.Add(channel);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new AppException(ErrorCodes.Common.Conflict, ex.Message);
        }
    }
}
