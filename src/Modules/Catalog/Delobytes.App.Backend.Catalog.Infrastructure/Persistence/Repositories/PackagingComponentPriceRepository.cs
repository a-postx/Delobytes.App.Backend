using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class PackagingComponentPriceRepository : IPackagingComponentPriceRepository
{
    private readonly CatalogDbContext _context;

    public PackagingComponentPriceRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<PackagingComponentPrice?> GetActiveByComponentIdAsync(Guid componentId, CancellationToken ct)
    {
        return _context.PackagingComponentPrices
            .Include(p => p.Supplier)
            .FirstOrDefaultAsync(p => p.PackagingComponentId == componentId && p.IsActive, ct);
    }

    public Task<PackagingComponentPrice?> GetLatestByComponentIdAsync(Guid componentId, CancellationToken ct)
    {
        return _context.PackagingComponentPrices
            .Include(p => p.Supplier)
            .Where(p => p.PackagingComponentId == componentId)
            .OrderByDescending(p => p.ValidFrom)
            .ThenByDescending(p => p.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(PackagingComponentPrice price)
    {
        _context.PackagingComponentPrices.Add(price);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
