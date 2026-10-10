using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

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
            .Include(r => r.Product)
            .OrderByDescending(r => r.ValidFrom)
            .ToListAsync(ct);
    }

    public async Task<(int TotalCount, IReadOnlyList<ProductWorkRate> Items)> GetPagedByProductAsync(
        ProductWorkRateGroupFilter filter,
        string? search,
        int? skipProducts,
        int? takeProducts,
        string? sortBy,
        bool descending,
        CancellationToken ct)
    {
        // Two queries instead of one GroupBy: grouping with paging, ordering by a joined entity's
        // name and a collection include translates into SQL the database is free to re-plan, and
        // the group order in the response would then depend on that plan. Here the page of products
        // is decided once, and the versions are read for exactly those products.
        IQueryable<Product> query = ApplyGroupFilter(ApplySearch(_context.Products.AsNoTracking(), search), filter);

        int totalCount = await query.CountAsync(ct);

        IQueryable<Product> ordered = ApplyOrdering(query, sortBy, descending);

        if (skipProducts.HasValue)
        {
            ordered = ordered.Skip(skipProducts.Value);
        }

        if (takeProducts.HasValue)
        {
            ordered = ordered.Take(takeProducts.Value);
        }

        List<Guid> productIds = await ordered.Select(p => p.Id).ToListAsync(ct);

        if (productIds.Count == 0)
        {
            return (totalCount, Array.Empty<ProductWorkRate>());
        }

        // A page of products carries every version of each of them, so the request asks for the
        // id list rather than for rows.
        List<ProductWorkRate> rates = await _context.ProductWorkRates
            .AsNoTracking()
            .Include(r => r.Product)
            .Where(r => productIds.Contains(r.ProductId))
            .ToListAsync(ct);

        // The order is rebuilt in memory from the page order: SQL's ORDER BY over Product.Name
        // follows the database collation, which is not guaranteed to rank names the way the server
        // ranked them when it built the page.
        Dictionary<Guid, int> pageIndex = productIds
            .Select((id, index) => new { id, index })
            .ToDictionary(x => x.id, x => x.index);

        List<ProductWorkRate> orderedRates = rates
            .OrderBy(r => pageIndex[r.ProductId])
            .ThenByDescending(r => r.ValidFrom)
            .ThenByDescending(r => r.CreatedAt)
            .ToList();

        return (totalCount, orderedRates);
    }

    public async Task<(int Active, int Inactive, int All)> GetGroupCountsAsync(string? search, CancellationToken ct)
    {
        // The counters describe products, not versions: a product with an active and an inactive
        // version is counted once under each. They are derived from the same search-filtered set the
        // list shows, otherwise the filter labels would advertise totals the user cannot reach.
        IQueryable<Product> query = ApplySearch(_context.Products.AsNoTracking(), search);

        // Deliberately three statements rather than one grouped projection: each is a plain COUNT
        // over an EXISTS subquery, which both providers translate predictably, and the counters are
        // only requested with the filter labels, not on every page load.
        int active = await query
            .Where(p => p.ProductWorkRates.Any(r => r.IsActive))
            .CountAsync(ct);

        int inactive = await query
            .Where(p => p.ProductWorkRates.Any(r => !r.IsActive))
            .CountAsync(ct);

        // "All" is the two groups merged, not a third scan: a product matches it when it carries a
        // version of either status, and a product without versions is excluded by both conditions.
        int all = await query
            .Where(p => p.ProductWorkRates.Any())
            .CountAsync(ct);

        return (active, inactive, all);
    }

    public async Task<IReadOnlyList<ProductWorkRate>> GetByProductIdAsync(Guid productId, CancellationToken ct)
    {
        return await _context.ProductWorkRates
            .Include(r => r.Product)
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

    private static IQueryable<Product> ApplySearch(IQueryable<Product> query, string? search)
    {
        string? term = CatalogSearchTerm.Normalize(search);

        if (term is null)
        {
            return query;
        }

        // Case-insensitive substring match, translated by projecting both sides into lower case so
        // the SQL is portable. A leading wildcard cannot use a B-tree index: the scan stays inside
        // the tenant, which is acceptable at the current product counts.
        string lowered = term.ToLowerInvariant();

        return query.Where(p => p.Name.ToLower().Contains(lowered) || p.Sku.ToLower().Contains(lowered));
    }

    private static IQueryable<Product> ApplyGroupFilter(IQueryable<Product> query, ProductWorkRateGroupFilter filter)
    {
        // All three branches require at least one version, so a product with no rate version never
        // appears -- the filter reads the versions, not the catalog.
        return filter switch
        {
            ProductWorkRateGroupFilter.Active => query.Where(p => p.ProductWorkRates.Any(r => r.IsActive)),
            ProductWorkRateGroupFilter.Inactive => query.Where(p => p.ProductWorkRates.Any(r => !r.IsActive)),
            _ => query.Where(p => p.ProductWorkRates.Any()),
        };
    }

    private static IQueryable<Product> ApplyOrdering(IQueryable<Product> query, string? sortBy, bool descending)
    {
        // Whitelist only: a client string never reaches EF.Property or an expression build. The date
        // keys describe the newest version of the product, which is the row the list displays: a
        // correlated subquery rather than an aggregate over the collection, because that is the form
        // both database providers translate.
        switch (sortBy?.ToLowerInvariant())
        {
            case "updatedat":
                return Order(
                    query,
                    p => p.ProductWorkRates
                        .OrderByDescending(r => r.UpdatedAt ?? r.CreatedAt)
                        .Select(r => (DateTimeOffset?)(r.UpdatedAt ?? r.CreatedAt))
                        .FirstOrDefault(),
                    descending);
            case "validfrom":
                return Order(
                    query,
                    p => p.ProductWorkRates
                        .OrderByDescending(r => r.ValidFrom)
                        .Select(r => (DateOnly?)r.ValidFrom)
                        .FirstOrDefault(),
                    descending);
            case "productname":
            default:
                return Order(query, p => p.Name, descending);
        }
    }

    private static IQueryable<Product> Order<TKey>(
        IQueryable<Product> query,
        Expression<Func<Product, TKey>> keySelector,
        bool descending)
    {
        // ThenBy(Id) keeps paging stable when the sort key has duplicates.
        return descending
            ? query.OrderByDescending(keySelector).ThenBy(p => p.Id)
            : query.OrderBy(keySelector).ThenBy(p => p.Id);
    }
}
