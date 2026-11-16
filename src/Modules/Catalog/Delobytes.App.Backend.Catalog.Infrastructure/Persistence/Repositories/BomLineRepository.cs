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

    public async Task<IReadOnlyList<BomLine>> GetOpenByProductIdAsync(Guid productId, CancellationToken ct)
    {
        return await _context.BomLines
            .Where(b => b.ProductId == productId && b.ValidTo == null)
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
        // The component is included because the cost calculation needs its name and category;
        // without it every cost line would have to resolve the component separately.
        //
        // The upper bound is what keeps a removed component out of the composition: filtering on
        // ValidFrom alone resolves every version that ever started, so a component dropped from the
        // composition would still be charged for every later date. The interval is half-open —
        // ValidTo is the first date on which the version no longer applies.
        List<BomLine> candidates = await _context.BomLines
            .Where(b => b.ProductId == productId
                && b.ValidFrom <= asOf
                && (b.ValidTo == null || asOf < b.ValidTo))
            .Include(b => b.Component)
            .ToListAsync(ct);

        // The group is a safety net, not the rule: intervals of one component must not overlap, and
        // writes keep them that way, but a row inserted by a future path that skipped the rule must
        // still resolve to one version rather than double the component quantity.
        return candidates
            .GroupBy(b => b.ComponentId)
            .Select(group => group.OrderByDescending(b => b.ValidFrom).ThenByDescending(b => b.CreatedAt).First())
            .ToList();
    }

    public void Add(BomLine line)
    {
        _context.BomLines.Add(line);
    }

    public async Task<IReadOnlyList<Guid>> GetProductIdsByComponentIdAsync(Guid componentId, CancellationToken ct)
    {
        return await _context.BomLines
            .Where(b => b.ComponentId == componentId && b.ValidTo == null)
            .Select(b => b.ProductId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
