using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class PackagingComponentRepository : IPackagingComponentRepository
{
    private readonly CatalogDbContext _context;

    public PackagingComponentRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<PackagingComponent?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.PackagingComponents.FirstOrDefaultAsync(pc => pc.Id == id, ct);
    }

    public async Task<IReadOnlyList<PackagingComponent>> GetAllAsync(CancellationToken ct)
    {
        return await _context.PackagingComponents
            .OrderBy(pc => pc.Name)
            .ToListAsync(ct);
    }

    public void Add(PackagingComponent component)
    {
        _context.PackagingComponents.Add(component);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
