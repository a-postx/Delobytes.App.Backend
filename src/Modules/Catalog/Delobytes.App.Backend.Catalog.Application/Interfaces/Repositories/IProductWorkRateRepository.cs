using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductWorkRateRepository
{
    Task<ProductWorkRate?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetAllAsync(CancellationToken ct);

    /// <summary>
    /// Returns one page of rate versions, paged by product rather than by row: the page holds every
    /// version of the products that fall on it, so a version history is never split across pages.
    /// The product navigation is loaded, because the list response carries each product's name and SKU.
    /// </summary>
    /// <param name="filter">Group-level status filter.</param>
    /// <param name="search">Free-text filter over product name and SKU, trimmed and capped at 200
    /// characters; null, empty or whitespace-only means "no filter".</param>
    /// <param name="skipProducts">Products to skip; null means none. Applied after ordering.</param>
    /// <param name="takeProducts">Products to take; null means no limit. Applied after ordering.</param>
    /// <param name="sortBy">Sort key resolved against a fixed whitelist (productName, updatedAt,
    /// validFrom); unknown keys fall back to productName. The date keys describe the latest version
    /// of each product.</param>
    /// <param name="descending">True for descending order, false for ascending.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The total number of matching products and their rate versions, ordered by product
    /// as on the page and, within a product, with the newest version first.</returns>
    Task<(int TotalCount, IReadOnlyList<ProductWorkRate> Items)> GetPagedByProductAsync(
        ProductWorkRateGroupFilter filter,
        string? search,
        int? skipProducts,
        int? takeProducts,
        string? sortBy,
        bool descending,
        CancellationToken ct);

    /// <summary>
    /// Returns how many products match each of the group filters, under the same search term the
    /// list was given, so the filter labels describe the current search result set. Products without
    /// any rate version are never counted.
    /// </summary>
    /// <param name="search">Free-text filter over product name and SKU, normalised the same way as
    /// in <see cref="GetPagedByProductAsync"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<(int Active, int Inactive, int All)> GetGroupCountsAsync(
        string? search,
        CancellationToken ct);

    Task<IReadOnlyList<ProductWorkRate>> GetByProductIdAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Returns the version currently in force for the product (IsActive = true), so that a new
    /// version appended through POST can supersede it. At most one row is expected to match;
    /// see the repository implementation for the tie-break used if more than one ever does.
    /// </summary>
    /// <param name="productId">Product to look up.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The active version, or null when the product currently has none.</returns>
    Task<ProductWorkRate?> GetActiveByProductIdAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Returns the assembly output rate version that was effective on <paramref name="asOf"/>.
    /// Deactivation is deliberately ignored, so a soft-deleted rate keeps resolving for dates on
    /// which it was the effective one. Pass the current date to honour deletion in new calculations.
    /// </summary>
    /// <param name="productId">Product to look up.</param>
    /// <param name="asOf">Date the rate is resolved against.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The effective rate version, or null when none existed by that date.</returns>
    Task<ProductWorkRate?> GetEffectiveAtAsync(Guid productId, DateOnly asOf, CancellationToken ct);

    Task<IReadOnlyList<Guid>> GetProductIdsByWorkRateIdAsync(Guid workRateId, CancellationToken ct);

    void Add(ProductWorkRate rate);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
