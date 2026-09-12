using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ProductWorkRateRepository : IProductWorkRateRepository
{
    private readonly CatalogDbContext _context;

    public ProductWorkRateRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<ProductWorkRate?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.ProductWorkRates.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<ProductWorkRate>> GetAllAsync(CancellationToken ct)
    {
        return await _context.ProductWorkRates
            .OrderByDescending(r => r.ValidFrom)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductWorkRate>> GetByProductIdAsync(Guid productId, CancellationToken ct)
    {
        return await _context.ProductWorkRates
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.ValidFrom)
            .ToListAsync(ct);
    }

    public void Add(ProductWorkRate rate)
    {
        _context.ProductWorkRates.Add(rate);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
