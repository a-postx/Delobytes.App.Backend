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
    /// <returns>The unpaged total count and the requested slice of products.</returns>
    Task<(int TotalCount, IReadOnlyList<Product> Items)> GetPagedAsync(
        ProductStatus? status,
        int? skip,
        int? take,
        string? sortBy,
        bool descending,
        CancellationToken ct);

    /// <summary>
    /// Returns how many products exist per status tab, ignoring any status filter:
    /// Active, Archived and the total across all statuses.
    /// </summary>
    Task<(int Active, int Archived, int All)> GetStatusCountsAsync(CancellationToken ct);

    void Add(Product product);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
