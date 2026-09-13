using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ComponentPriceRepository : IComponentPriceRepository
{
    private readonly CatalogDbContext _context;

    public ComponentPriceRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<ComponentPrice?> GetActiveByComponentIdAsync(Guid componentId, CancellationToken ct)
    {
        return _context.ComponentPrices
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.ComponentId == componentId && p.IsActive, ct);
    }

    public Task<ComponentPrice?> GetLatestByComponentIdAsync(Guid componentId, CancellationToken ct)
    {
        return _context.ComponentPrices
            .Include(p => p.Supplier)
            .Where(p => p.ComponentId == componentId)
            .OrderByDescending(p => p.ValidFrom)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(ComponentPrice price)
    {
        _context.ComponentPrices.Add(price);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
