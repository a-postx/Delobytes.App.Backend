using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _context;

    public ProductRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Products
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public Task<Product?> GetWithChannelProductsByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Products
            .Include(p => p.ChannelProducts)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<Product>> GetAllByStatusAsync(ProductStatus? status, CancellationToken ct)
    {
        ProductStatus filterStatus = status ?? ProductStatus.Active;

        return await _context.Products
            .Where(p => p.Status == filterStatus)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public void Add(Product product)
    {
        _context.Products.Add(product);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
