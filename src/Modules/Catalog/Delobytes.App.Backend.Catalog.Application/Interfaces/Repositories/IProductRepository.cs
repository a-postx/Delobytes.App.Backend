using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<Product?> GetWithChannelProductsByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Returns products ordered by name. A null status means "all statuses".</summary>
    Task<IReadOnlyList<Product>> GetAllByStatusAsync(ProductStatus? status, CancellationToken ct);

    /// <summary>
    /// Returns one page of products together with the total count of matching rows.
    /// </summary>
    /// <param name="status">Status filter; null means "all statuses".</param>
    /// <param name="skip">Rows to skip; null or negative means none. Applied after ordering.</param>
    /// <param name="take">Rows to take; null means no limit. Applied after ordering.</param>
    /// <param name="sortBy">Sort key resolved against a fixed whitelist (name, sku, status, createdAt,
    /// updatedAt); unknown keys fall back to name. Sorting by updatedAt uses CreatedAt for products
    /// that have never been edited.</param>
    /// <param name="descending">True for descending order, false for ascending.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="search">Free-text filter applied on top of <paramref name="status"/> with AND:
    /// a product matches when its name or its SKU contains the term, case-insensitively. The term is
    /// trimmed and truncated to 200 characters; null, empty or whitespace-only means "no filter".
    /// Paging is applied after this filter, so the returned total count describes the filtered set.</param>
    /// <returns>The unpaged total count and the requested slice of products.</returns>
    Task<(int TotalCount, IReadOnlyList<Product> Items)> GetPagedAsync(
        ProductStatus? status,
        int? skip,
        int? take,
        string? sortBy,
        bool descending,
        CancellationToken ct,
        string? search = null);

    /// <summary>
    /// Returns how many products exist per status tab:
    /// Active, Archived and the total across all statuses.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="search">The same free-text filter the list query was given, so the tab counters
    /// describe the current search result set instead of the whole catalog: a product is counted when
    /// its name or SKU contains the trimmed, 200-character-truncated term, case-insensitively. Null,
    /// empty or whitespace-only means "count everything".</param>
    Task<(int Active, int Archived, int All)> GetStatusCountsAsync(CancellationToken ct, string? search = null);

    void Add(Product product);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
