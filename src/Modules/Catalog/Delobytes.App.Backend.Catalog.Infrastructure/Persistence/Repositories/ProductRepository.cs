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
        return await BuildListQuery(status, null)
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
        CancellationToken ct,
        string? search = null,
        bool includeWorkRateCoverage = false)
    {
        IQueryable<Product> query = BuildListQuery(status, search, includeWorkRateCoverage);
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

    public async Task<(int Active, int Archived, int All)> GetStatusCountsAsync(CancellationToken ct, string? search = null)
    {
        // The counters must be derived from the same filtered set the list is showing, otherwise
        // the tabs advertise totals for products the user cannot see under an active search.
        IQueryable<Product> query = ApplySearch(_context.Products.AsNoTracking(), search);

        // One grouped statement instead of three round trips; the tab counters only need
        // these two statuses plus the total.
        List<StatusCountRow> rows = await query
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

    private IQueryable<Product> BuildListQuery(ProductStatus? status, string? search, bool includeWorkRateCoverage = false)
    {
        IQueryable<Product> query = _context.Products
            .Include(p => p.Barcodes)
            .Include(p => p.Photos)
            .Include(p => p.ChannelProducts).ThenInclude(cp => cp.Channel);

        // The coverage flag is projected in memory from the loaded collection, and the list view
        // is the only consumer that needs it -- loading the versions for everyone would add a
        // fourth collection include to every catalog request.
        if (includeWorkRateCoverage)
        {
            query = query.Include(p => p.ProductWorkRates);
        }

        // Null means "no filter": callers that build the full catalog view need every
        // status (active, archived, deletion states) to compute per-tab counters.
        if (status.HasValue)
        {
            ProductStatus filterStatus = status.Value;
            query = query.Where(p => p.Status == filterStatus);
        }

        return ApplySearch(query, search);
    }

    private static IQueryable<Product> ApplySearch(IQueryable<Product> query, string? search)
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

        return query.Where(p => p.Name.ToLower().Contains(lowered) || p.Sku.ToLower().Contains(lowered));
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
