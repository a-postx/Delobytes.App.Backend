namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetAllProductWorkRates;

public class GetAllProductWorkRatesResponse
{
    /// <summary>
    /// Rate versions of the products on the requested page. A page carries every version of each
    /// product that fell on it, so this list is longer than <see cref="TotalCount"/> whenever some
    /// product has more than one version.
    /// </summary>
    public List<ProductWorkRateDto> Items { get; init; } = [];

    /// <summary>
    /// Number of distinct products matching the filter, not the number of rows in
    /// <see cref="Items"/>: a page carries every version of each of its products.
    /// </summary>
    public int TotalCount { get; init; }

    /// <summary>Page that was served, 1-based. Always 1 when the request omitted a page number.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Page size that was applied, after clamping.</summary>
    public int PageSize { get; init; }

    /// <summary>Present only when the request asked for counts. Counted per product, not per version.</summary>
    public ProductWorkRateGroupCounts? StatusCounts { get; init; }
}

/// <summary>
/// Group-level totals: each counter is the number of products matching that filter, counted the
/// same way as the list itself, so a product with an active and an inactive version counts once
/// in <see cref="Active"/> and once in <see cref="Inactive"/>.
/// </summary>
public class ProductWorkRateGroupCounts
{
    /// <summary>Products with at least one active version.</summary>
    public int Active { get; init; }

    /// <summary>Products with at least one inactive version.</summary>
    public int Inactive { get; init; }

    /// <summary>
    /// Products with at least one version of any status. Products that have no rate version at all
    /// are not counted: the query reads the ProductWorkRates table.
    /// </summary>
    public int All { get; init; }
}
