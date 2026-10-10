using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetAllProductWorkRates;

/// <summary>
/// Returns the product work rate list, one page of products with every version of each of them.
/// </summary>
/// <remarks>
/// Paging counts products, not versions: a product with several versions is never split across a
/// page boundary, otherwise a group would break apart and the archived-version counter would lie.
/// <see cref="GetAllProductWorkRatesResponse.TotalCount"/> is therefore the number of matching
/// products, not the number of rows returned.
/// Paging is opt-in: when <see cref="Page"/> is null the query reproduces the original behaviour
/// and returns every version of every product without <c>Skip</c>/<c>Take</c>.
/// </remarks>
public class GetAllProductWorkRatesQuery : IRequest<GetAllProductWorkRatesResponse>
{
    /// <summary>
    /// Optional free-text filter over the product name and SKU: case-insensitive substring match,
    /// a product is kept when either field contains the term. Blank or whitespace-only values mean
    /// "no search". The value is trimmed and capped at 200 characters.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Group-level status filter. Note that the source is the ProductWorkRates table, so
    /// <see cref="ProductWorkRateGroupFilter.All"/> means "every product that has at least one
    /// version", not "every product in the catalog": a product without any rate version has no
    /// rows here and cannot be returned by this endpoint.
    /// </summary>
    public ProductWorkRateGroupFilter Status { get; set; } = ProductWorkRateGroupFilter.Active;

    /// <summary>
    /// Optional 1-based page number. Null means "no pagination": the whole list is returned.
    /// Values below 1 are treated as page 1.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Number of products per page, clamped to 1..200. Only applied when <see cref="Page"/> is set.
    /// </summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Sort key, resolved against a whitelist: productName (default), updatedAt, validFrom.
    /// Unknown or missing values fall back to productName. The date keys describe the latest
    /// version of each product.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Sort direction: "asc" (default) or "desc". Any other value falls back to ascending.
    /// </summary>
    public string? SortDir { get; set; }

    /// <summary>
    /// When true, <see cref="GetAllProductWorkRatesResponse.StatusCounts"/> is populated with
    /// per-group totals computed over the whole filtered set, so the filter labels stay correct
    /// while the list itself is paged.
    /// </summary>
    public bool IncludeCounts { get; set; }
}
