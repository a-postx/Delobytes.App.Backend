using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ProductWorkRateRepository : IProductWorkRateRepository
{
    private readonly CatalogDbContext _context;

    public ProductWorkRateRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<ProductWorkRate?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.ProductWorkRates.FirstOrDefaultAsync(r => r.Id == id, ct);
    }

    public async Task<IReadOnlyList<ProductWorkRate>> GetAllAsync(CancellationToken ct)
    {
        return await _context.ProductWorkRates
            .OrderByDescending(r => r.ValidFrom)
            .ToListAsync(ct);
    }

    public async Task<IReadOnlyList<ProductWorkRate>> GetByProductIdAsync(Guid productId, CancellationToken ct)
    {
        return await _context.ProductWorkRates
            .Where(r => r.ProductId == productId)
            .OrderByDescending(r => r.ValidFrom)
            .ToListAsync(ct);
    }

    public Task<ProductWorkRate?> GetActiveByProductIdAsync(Guid productId, CancellationToken ct)
    {
        // Normally at most one row is IsActive per product. The ordering is a defensive tie-break,
        // not an assumption that it cannot happen: it mirrors GetEffectiveAtAsync so that, even if
        // data ever drifted (e.g. a manual fix in the database), the most recently started version
        // wins deterministically instead of leaving the choice to query-plan-dependent ordering.
        return _context.ProductWorkRates
            .Where(r => r.ProductId == productId && r.IsActive)
            .OrderByDescending(r => r.ValidFrom)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public Task<ProductWorkRate?> GetEffectiveAtAsync(Guid productId, DateOnly asOf, CancellationToken ct)
    {
        // IsActive is intentionally not filtered. Note that this method cannot tell a version
        // superseded by a newer one from a soft-deleted one, so a deleted rate keeps resolving for
        // every date from its ValidFrom onwards. The calculation treats that as the data it was asked for.
        return _context.ProductWorkRates
            .Where(r => r.ProductId == productId && r.ValidFrom <= asOf)
            .OrderByDescending(r => r.ValidFrom)
            .ThenByDescending(r => r.CreatedAt)
            .FirstOrDefaultAsync(ct);
    }

    public void Add(ProductWorkRate rate)
    {
        _context.ProductWorkRates.Add(rate);
    }

    public async Task<IReadOnlyList<Guid>> GetProductIdsByWorkRateIdAsync(Guid workRateId, CancellationToken ct)
    {
        return await _context.ProductWorkRates
            .Where(r => r.WorkRateId == workRateId && r.IsActive)
            .Select(r => r.ProductId)
            .Distinct()
            .ToListAsync(ct);
    }

    public Task<int> SaveChangesAsync(CancellationToken ct)
    {
        return _context.SaveChangesWithConflictTranslationAsync(ct);
    }
}
