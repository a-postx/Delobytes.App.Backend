using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;

/// <summary>
/// Returns the product list, optionally filtered by status and search term, and optionally paged.
/// </summary>
/// <remarks>
/// Paging is opt-in: when <see cref="Page"/> is null the query reproduces the original
/// behaviour and returns the whole matching list without <c>Skip</c>/<c>Take</c> — views that
/// need every status at once (work rates, channel costs) rely on that. When <see cref="Page"/>
/// is set, <c>Skip</c>/<c>Take</c> are applied after filtering and ordering, and
/// <see cref="GetProductsResponse.TotalCount"/> describes the unpaged result set.
/// Out-of-range <see cref="PageSize"/> is clamped, never rejected.
/// </remarks>
public class GetProductsQuery : IRequest<GetProductsResponse>
{
    /// <summary>
    /// Optional status filter. When omitted, products of all statuses are returned and the
    /// caller is responsible for filtering (per-status counts are returned when
    /// <see cref="IncludeCounts"/> is set).
    /// </summary>
    public ProductStatus? Status { get; set; }

    /// <summary>
    /// Optional free-text filter over the product name and SKU: case-insensitive substring match,
    /// a product is kept when either field contains the term. Combined with <see cref="Status"/>
    /// using AND. Blank or whitespace-only values are treated as "no search". The value is trimmed
    /// and capped at 200 characters.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Optional 1-based page number. Null means "no pagination": the full list is returned.
    /// Values below 1 are treated as page 1.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Page size, clamped to 1..200. Only applied when <see cref="Page"/> is set.
    /// </summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Sort key, resolved against a whitelist: name, sku, status, createdAt.
    /// Unknown or missing values fall back to name.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Sort direction: "asc" (default) or "desc". Any other value falls back to ascending.
    /// </summary>
    public string? SortDir { get; set; }

    /// <summary>
    /// When true, <see cref="GetProductsResponse.StatusCounts"/> is populated with per-status
    /// totals computed over the whole (unpaged) result set, so tab counters stay correct
    /// while the list itself is paged. The same search filter is applied, so the counters
    /// describe the current search results rather than the whole catalog.
    /// </summary>
    public bool IncludeCounts { get; set; }

    /// <summary>
    /// When true, every returned item carries <see cref="ProductItem.HasActiveWorkRate"/>, telling
    /// the caller whether the product has at least one active assembly output rate. Off by default:
    /// it costs an extra collection include per product, and only the product picker needs it.
    /// </summary>
    public bool IncludeWorkRateCoverage { get; set; }
}
