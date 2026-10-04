using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ProductCostSnapshotRepository : IProductCostSnapshotRepository
{
    private readonly CatalogDbContext _context;

    public ProductCostSnapshotRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlySet<Guid>> GetExistingProductIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        DateOnly asOfDate,
        string triggerReason,
        CancellationToken ct)
    {
        List<Guid> existingProductIds = await _context.ProductCostSnapshots
            .Where(snapshot => productIds.Contains(snapshot.ProductId)
                && snapshot.AsOfDate == asOfDate
                && snapshot.TriggerReason == triggerReason)
            .Select(snapshot => snapshot.ProductId)
            .ToListAsync(ct);

        HashSet<Guid> result = existingProductIds.ToHashSet();
        return result;
    }

    public async Task<(int TotalCount, IReadOnlyList<ProductCostSnapshot> Items)> GetHistoryAsync(
        Guid productId,
        int skip,
        int take,
        CancellationToken ct)
    {
        IQueryable<ProductCostSnapshot> query = _context.ProductCostSnapshots
            .Where(snapshot => snapshot.ProductId == productId)
            .OrderByDescending(snapshot => snapshot.AsOfDate)
            .ThenByDescending(snapshot => snapshot.CalculatedAt);
        int totalCount = await query.CountAsync(ct);
        List<ProductCostSnapshot> items = await query.Skip(skip).Take(take).ToListAsync(ct);
        return (totalCount, items);
    }

    public void Add(ProductCostSnapshot snapshot)
    {
        _context.ProductCostSnapshots.Add(snapshot);
    }
}
