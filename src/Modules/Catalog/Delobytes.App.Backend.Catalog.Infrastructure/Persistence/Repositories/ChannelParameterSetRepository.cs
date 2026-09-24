using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ChannelParameterSetRepository : IChannelParameterSetRepository
{
    private readonly CatalogDbContext _context;

    public ChannelParameterSetRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<ChannelParameterSet?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.ChannelParameterSets
            .FirstOrDefaultAsync(cps => cps.Id == id, ct);
    }

    public Task<List<ChannelParameterSet>> GetByChannelIdAsync(Guid channelId, CancellationToken ct)
    {
        return _context.ChannelParameterSets
            .Where(cps => cps.ChannelId == channelId)
            .OrderByDescending(cps => cps.ValidFrom)
            .ToListAsync(ct);
    }

    public Task<ChannelParameterSet?> GetActiveByChannelIdAsync(Guid channelId, CancellationToken ct)
    {
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        return _context.ChannelParameterSets
            .Where(cps => cps.ChannelId == channelId)
            .Where(cps => cps.ValidFrom <= today)
            .OrderByDescending(cps => cps.ValidFrom)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(ChannelParameterSet parameterSet)
    {
        _context.ChannelParameterSets.Add(parameterSet);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
