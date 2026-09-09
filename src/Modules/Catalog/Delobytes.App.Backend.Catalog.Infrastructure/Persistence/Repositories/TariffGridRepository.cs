using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class TariffGridRepository : ITariffGridRepository
{
    private readonly CatalogDbContext _context;

    public TariffGridRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<TariffGrid?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.TariffGrids.FirstOrDefaultAsync(tg => tg.Id == id, ct);
    }

    public Task<TariffGrid?> GetByIdWithEntriesAsync(Guid id, CancellationToken ct)
    {
        return _context.TariffGrids
            .Include(tg => tg.Entries)
            .FirstOrDefaultAsync(tg => tg.Id == id, ct);
    }

    public async Task<IReadOnlyList<TariffGrid>> GetAllAsync(CancellationToken ct)
    {
        return await _context.TariffGrids
            .OrderBy(tg => tg.TariffType)
            .ThenByDescending(tg => tg.ValidFrom)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<TariffGrid>> GetByTypeAsync(TariffType tariffType, CancellationToken ct)
    {
        return await _context.TariffGrids
            .Where(tg => tg.TariffType == tariffType)
            .OrderByDescending(tg => tg.ValidFrom)
            .ToListAsync(ct);
    }

    public void Add(TariffGrid grid)
    {
        _context.TariffGrids.Add(grid);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
