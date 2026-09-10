using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class RawMaterialRateRepository : IRawMaterialRateRepository
{
    private readonly CatalogDbContext _context;

    public RawMaterialRateRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<RawMaterialRate?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.RawMaterialRates.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<RawMaterialRate>> GetAllAsync(CancellationToken ct)
    {
        return await _context.RawMaterialRates
            .Where(r => r.IsActive)
            .OrderBy(r => r.ProductId)
            .ThenByDescending(r => r.ValidFrom)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<RawMaterialRate>> GetByProductIdAsync(Guid productId, CancellationToken ct)
    {
        return await _context.RawMaterialRates
            .Where(r => r.ProductId == productId && r.IsActive)
            .OrderByDescending(r => r.ValidFrom)
            .ToListAsync(ct);
    }

    public void Add(RawMaterialRate rate)
    {
        _context.RawMaterialRates.Add(rate);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
