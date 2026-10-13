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

    public async Task<IReadOnlyList<Component>> GetAllAsync(CancellationToken ct)
    {
        return await _context.Components
            .Include(pc => pc.Prices)
                .ThenInclude(p => p.Supplier)
            .OrderBy(pc => pc.Name)
            .ToListAsync(ct);
    }

    public async Task<(int TotalCount, IReadOnlyList<Component> Items)> GetPagedAsync(
        int? skip,
        int? take,
        string? sortBy,
        bool descending,
        CancellationToken ct,
        string? search = null)
    {
        IQueryable<Component> query = BuildListQuery(search);
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

    private IQueryable<Component> BuildListQuery(string? search)
    {
        IQueryable<Component> query = _context.Components
            .Include(pc => pc.Prices)
                .ThenInclude(p => p.Supplier);

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
                // The active price (if any) carries the supplier; a component without one sorts
                // as if its supplier name were null.
                return Order(
                    query,
                    c => c.Prices
                        .Where(p => p.IsActive)
                        .Select(p => p.Supplier != null ? p.Supplier.Name : null)
                        .FirstOrDefault(),
                    descending);
            case "validfrom":
                return Order(
                    query,
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
}
