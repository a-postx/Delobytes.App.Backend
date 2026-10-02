using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class SupplierRepository : ISupplierRepository
{
    private readonly CatalogDbContext _context;

    public SupplierRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<Supplier?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Suppliers.FirstOrDefaultAsync(s => s.Id == id, ct);
    }

    public async Task<IReadOnlyList<Supplier>> GetAllAsync(CancellationToken ct)
    {
        return await _context.Suppliers
            .OrderBy(s => s.Name)
            .ToListAsync(ct);
    }

    public void Add(Supplier supplier)
    {
        _context.Suppliers.Add(supplier);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        // Concurrency and unique-constraint violations surface as 409 with a domain code,
        // not as a raw DbUpdateException that middleware can only render as 500.
        return await _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
