using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
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

    public async Task<IReadOnlyList<Component>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)
    {
        // A preview of an empty draft would otherwise issue a pointless round trip for zero rows.
        if (ids.Count == 0)
        {
            return new List<Component>();
        }

        List<Component> components = await _context.Components
            .Where(component => ids.Contains(component.Id))
            .ToListAsync(ct);

        return components;
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

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        // Concurrency and unique-constraint violations surface as 409 with a domain code,
        // not as a raw DbUpdateException that middleware can only render as 500.
        return await _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
