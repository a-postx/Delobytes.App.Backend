using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
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
        try
        {
            return await _context.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new ConcurrencyException(
                "Данные изменены другим пользователем. Обновите страницу и повторите попытку.");
        }
    }
}
