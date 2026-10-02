using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class CostTypeRepository : ICostTypeRepository
{
    private readonly CatalogDbContext _context;

    public CostTypeRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<CostType?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.CostTypes.FirstOrDefaultAsync(ct2 => ct2.Id == id, ct);
    }

    public async Task<IReadOnlyList<CostType>> GetAllAsync(CancellationToken ct)
    {
        return await _context.CostTypes
            .OrderBy(ct2 => ct2.Name)
            .ToListAsync(ct);
    }

    public void Add(CostType costType)
    {
        _context.CostTypes.Add(costType);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        // Concurrency and unique-constraint violations surface as 409 with a domain code,
        // not as a raw DbUpdateException that middleware can only render as 500.
        return await _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
