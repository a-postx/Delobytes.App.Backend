using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ComponentRepository : IComponentRepository
{
    private readonly CatalogDbContext _context;

    public ComponentRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<Component?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Components
            .FirstOrDefaultAsync(pc => pc.Id == id, ct);
    }

    public Task<Component?> GetWithPricesByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Components
            .Include(pc => pc.Prices)
                .ThenInclude(p => p.Supplier)
            .FirstOrDefaultAsync(pc => pc.Id == id, ct);
    }

    public async Task<IReadOnlyList<Component>> GetAllAsync(CancellationToken ct)
    {
        return await _context.Components
            .Include(pc => pc.Prices)
                .ThenInclude(p => p.Supplier)
            .OrderBy(pc => pc.Name)
            .ToListAsync(ct);
    }

    public void Add(Component component)
    {
        _context.Components.Add(component);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesAsync(ct);
    }
}
