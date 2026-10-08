using Delobytes.App.Backend.Catalog.Application.Exceptions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Infrastructure.Persistence;
using Delobytes.App.Backend.Contracts.Errors;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Persistence.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly CatalogDbContext _context;

    public ProductRepository(CatalogDbContext context)
    {
        _context = context;
    }

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Products
            .Include(p => p.Barcodes)
            .Include(p => p.PackingUnits)
            .Include(p => p.Photos)
            .Include(p => p.ChannelProducts).ThenInclude(cp => cp.Channel)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public Task<Product?> GetWithChannelProductsByIdAsync(Guid id, CancellationToken ct)
    {
        return _context.Products
            .Include(p => p.ChannelProducts)
            .Include(p => p.Photos)
            .FirstOrDefaultAsync(p => p.Id == id, ct);
    }

    public async Task<IReadOnlyList<Product>> GetAllByStatusAsync(ProductStatus? status, CancellationToken ct)
    {
        return await BuildListQuery(status)
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .ToListAsync(ct);
    }

    public async Task<(int TotalCount, IReadOnlyList<Product> Items)> GetPagedAsync(
        ProductStatus? status,
        int? skip,
        int? take,
        string? sortBy,
        bool descending,
        CancellationToken ct)
    {
        IQueryable<Product> query = BuildListQuery(status);
        int totalCount = await query.CountAsync(ct);
        IQueryable<Product> ordered = ApplyOrdering(query, sortBy, descending);

        if (skip.HasValue)
        {
            ordered = ordered.Skip(skip.Value);
        }

        if (take.HasValue)
        {
            ordered = ordered.Take(take.Value);
        }

        // Three collection includes on one query would otherwise multiply the joined rows;
        // split queries issue one statement per collection and keep each page cheap.
        List<Product> items = await ordered.AsSplitQuery().ToListAsync(ct);
        return (totalCount, items);
    }

    public async Task<(int Active, int Archived, int All)> GetStatusCountsAsync(CancellationToken ct)
    {
        // One grouped statement instead of three round trips; the tab counters only need
        // these two statuses plus the total.
        List<StatusCountRow> rows = await _context.Products
            .AsNoTracking()
            .GroupBy(p => p.Status)
            .Select(g => new StatusCountRow { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        int active = rows.Where(r => r.Status == ProductStatus.Active).Sum(r => r.Count);
        int archived = rows.Where(r => r.Status == ProductStatus.Archived).Sum(r => r.Count);
        int all = rows.Sum(r => r.Count);

        return (active, archived, all);
    }

    public void Add(Product product)
    {
        _context.Products.Add(product);
    }

    public async Task<int> SaveChangesAsync(CancellationToken ct)
    {
        // Concurrency and unique-constraint violations surface as 409 with a domain code,
        // not as a raw DbUpdateException that middleware can only render as 500.
        return await _context.SaveChangesWithConflictTranslationAsync(ct);
    }

    private IQueryable<Product> BuildListQuery(ProductStatus? status)
    {
        IQueryable<Product> query = _context.Products
            .Include(p => p.Barcodes)
            .Include(p => p.Photos)
            .Include(p => p.ChannelProducts).ThenInclude(cp => cp.Channel);

        // Null means "no filter": callers that build the full catalog view need every
        // status (active, archived, deletion states) to compute per-tab counters.
        if (status.HasValue)
        {
            ProductStatus filterStatus = status.Value;
            query = query.Where(p => p.Status == filterStatus);
        }

        return query;
    }

    private static IQueryable<Product> ApplyOrdering(IQueryable<Product> query, string? sortBy, bool descending)
    {
        // Whitelist only: a client string never reaches EF.Property or an expression build.
        switch (sortBy?.ToLowerInvariant())
        {
            case "sku":
                return Order(query, p => p.Sku, descending);
            case "status":
                return Order(query, p => p.Status, descending);
            case "createdat":
                return Order(query, p => p.CreatedAt, descending);
            case "updatedat":
                // A product that was never edited has a null UpdatedAt, and the list view shows
                // its creation moment in the "Изменено" column. Order by that same effective
                // value, otherwise such rows would sort to one end regardless of their date.
                return Order(query, p => p.UpdatedAt ?? p.CreatedAt, descending);
            case "name":
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

    private sealed class StatusCountRow
    {
        public ProductStatus Status { get; set; }

        public int Count { get; set; }
    }
}
