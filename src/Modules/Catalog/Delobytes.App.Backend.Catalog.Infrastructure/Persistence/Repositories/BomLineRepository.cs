using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class BomLineRepository : IBomLineRepository
{
    private readonly CatalogDbContext _context;

    public BomLineRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<BomLine?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.BomLines.FirstOrDefaultAsync(b => b.Id == id, ct);
    }

    public async Task<IReadOnlyList<BomLine>> GetActiveByProductIdAsync(Guid productId, CancellationToken ct)
    {
        return await _context.BomLines
            .Where(b => b.ProductId == productId && b.IsActive)
            .Include(b => b.Component)
            .ThenInclude(c => c.Prices.Where(p => p.IsActive))
            .OrderBy(b => b.Component.Name)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BomLine>> GetHistoryByProductIdAsync(Guid productId, CancellationToken ct)
    {
        return await _context.BomLines
            .Where(b => b.ProductId == productId)
            .Include(b => b.Component)
            .OrderBy(b => b.ComponentId)
            .ThenBy(b => b.ValidFrom)
            .ThenBy(b => b.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<BomLine>> GetEffectiveAtAsync(Guid productId, DateOnly asOf, CancellationToken ct)
    {
        List<BomLine> candidates = await _context.BomLines
            .Where(b => b.ProductId == productId && b.ValidFrom <= asOf)
            .ToListAsync(ct);

        return candidates
            .GroupBy(b => b.ComponentId)
            .Select(group => group.OrderByDescending(b => b.ValidFrom).ThenByDescending(b => b.CreatedAt).First())
            .ToList();
    }

    public void Add(BomLine line)
    {
        _context.BomLines.Add(line);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
