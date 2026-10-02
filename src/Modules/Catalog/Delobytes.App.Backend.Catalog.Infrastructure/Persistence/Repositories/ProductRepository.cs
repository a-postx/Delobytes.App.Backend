using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Errors;
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
            .Include(p => p.Barcodes)
            .Include(p => p.PackingUnits)
            .Include(p => p.Photos)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public Task<Product?> GetWithChannelProductsByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Products
            .Include(p => p.ChannelProducts)
            .Include(p => p.Photos)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<Product>> GetAllByStatusAsync(ProductStatus? status, CancellationToken ct)
    {
        IQueryable<Product> query = _context.Products
            .Include(p => p.Barcodes)
            .Include(p => p.Photos);

        // Null means "no filter": callers that build the full catalog view need every
        // status (active, archived, deletion states) to compute per-tab counters.
        if (status.HasValue)
        {
            ProductStatus filterStatus = status.Value;
            query = query.Where(p => p.Status == filterStatus);
        }

        return await query
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
    }

    public void Add(Product product)
    {
        _context.Products.Add(product);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        // Concurrency and unique-constraint violations surface as 409 with a domain code,
        // not as a raw DbUpdateException that middleware can only render as 500.
        return await _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
