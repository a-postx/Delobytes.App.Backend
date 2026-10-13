using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

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

    public async Task<(int TotalCount, IReadOnlyList<Component> Items)> GetPagedAsync(
        bool? isActive,
        int? skip,
        int? take,
        string? sortBy,
        bool descending,
        CancellationToken ct,
        string? search = null)
    {
        IQueryable<Component> query = BuildListQuery(isActive, search);
        int totalCount = await query.CountAsync(ct);
        IQueryable<Component> ordered = ApplyOrdering(query, sortBy, descending);

        if (skip.HasValue)
        {
            ordered = ordered.Skip(skip.Value);
        }

        if (take.HasValue)
        {
            ordered = ordered.Take(take.Value);
        }

        // The Prices collection include would otherwise multiply the joined rows together with
        // the ordering subquery; a split query keeps each page cheap.
        List<Component> items = await ordered.AsSplitQuery().ToListAsync(ct);
        return (totalCount, items);
    }

    public async Task<(int Active, int Inactive, int All)> GetStatusCountsAsync(CancellationToken ct, string? search = null)
    {
        // The counters must be derived from the same filtered set the list is showing, otherwise
        // the tabs advertise totals for components the user cannot see under an active search.
        IQueryable<Component> query = ApplySearch(_context.Components.AsNoTracking(), search);

        List<StatusCountRow> rows = await query
            .GroupBy(c => c.IsActive)
            .Select(g => new StatusCountRow { IsActive = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int active = rows.Where(r => r.IsActive).Sum(r => r.Count);
        int inactive = rows.Where(r => !r.IsActive).Sum(r => r.Count);
        int all = rows.Sum(r => r.Count);

        return (active, inactive, all);
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

    private IQueryable<Component> BuildListQuery(bool? isActive, string? search)
    {
        IQueryable<Component> query = _context.Components
            .Include(pc => pc.Prices)
                .ThenInclude(p => p.Supplier);

        if (isActive.HasValue)
        {
            bool filterActive = isActive.Value;
            query = query.Where(c => c.IsActive == filterActive);
        }

        return ApplySearch(query, search);
    }

    private static IQueryable<Component> ApplySearch(IQueryable<Component> query, string? search)
    {
        string? term = CatalogSearchTerm.Normalize(search);

        if (term is null)
        {
            return query;
        }

        // Case-insensitive substring match. Lower-casing both sides keeps this translatable to SQL
        // while staying provider-agnostic: EF.Functions.ILike works on PostgreSQL but throws on the
        // InMemory provider the repository tests run against.
        string lowered = term.ToLowerInvariant();

        return query.Where(c => c.Name.ToLower().Contains(lowered));
    }

    private static IQueryable<Component> ApplyOrdering(IQueryable<Component> query, string? sortBy, bool descending)
    {
        // Whitelist only: a client string never reaches EF.Property or an expression build.
        switch (sortBy?.ToLowerInvariant())
        {
            case "supplier":
                // The "missing" flag tracks the active price itself, not whether that price has a
                // supplier: a component without an active price sorts last, matching validFrom.
                return OrderWithMissingLast(
                    query,
                    c => c.Prices.Any(p => p.IsActive) ? 0 : 1,
                    c => c.Prices
                        .Where(p => p.IsActive)
                        .Select(p => p.Supplier != null ? p.Supplier.Name : null)
                        .FirstOrDefault(),
                    descending);
            case "validfrom":
                return OrderWithMissingLast(
                    query,
                    c => c.Prices.Any(p => p.IsActive) ? 0 : 1,
                    c => c.Prices
                        .Where(p => p.IsActive)
                        .Select(p => (DateOnly?)p.ValidFrom)
                        .FirstOrDefault(),
                    descending);
            case "createdat":
                return Order(query, c => c.CreatedAt, descending);
            case "name":
            default:
                return Order(query, c => c.Name, descending);
        }
    }

    private static IQueryable<Component> Order<TKey>(
        IQueryable<Component> query,
        Expression<Func<Component, TKey>> keySelector,
        bool descending)
    {
        // ThenBy(Id) keeps paging stable when the sort key has duplicates.
        return descending
            ? query.OrderByDescending(keySelector).ThenBy(c => c.Id)
            : query.OrderBy(keySelector).ThenBy(c => c.Id);
    }

    /// <summary>
    /// Orders by <paramref name="keySelector"/> while pinning components without a value
    /// (<paramref name="missingFlagSelector"/> equal to 1) to the end of the list regardless of
    /// direction. PostgreSQL and the InMemory test provider disagree on where a plain
    /// <c>ORDER BY</c> puts nulls, so the position is pinned explicitly with a flag column
    /// instead of relying on either provider's default.
    /// </summary>
    private static IQueryable<Component> OrderWithMissingLast<TKey>(
        IQueryable<Component> query,
        Expression<Func<Component, int>> missingFlagSelector,
        Expression<Func<Component, TKey>> keySelector,
        bool descending)
    {
        IOrderedQueryable<Component> ordered = query.OrderBy(missingFlagSelector);

        ordered = descending
            ? ordered.ThenByDescending(keySelector)
            : ordered.ThenBy(keySelector);

        return ordered.ThenBy(c => c.Id);
    }

    private sealed class StatusCountRow
    {
        public bool IsActive { get; set; }

        public int Count { get; set; }
    }
}
