using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates;

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

public class GetAllProductWorkRatesQueryHandler : IRequestHandler<GetAllProductWorkRatesQuery, GetAllProductWorkRatesResponse>
{
    private const int DefaultPageSize = 25;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 200;

    private readonly IProductWorkRateRepository _repository;

    public GetAllProductWorkRatesQueryHandler(IProductWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetAllProductWorkRatesResponse> Handle(
        GetAllProductWorkRatesQuery request,
        CancellationToken cancellationToken)
    {
        // No page requested means "give me everything": the legacy contract, kept for clients that
        // still expect the full list.
        int page = Math.Max(request.Page ?? 1, 1);
        int pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        int? skipProducts = request.Page.HasValue ? (page - 1) * pageSize : null;
        int? takeProducts = request.Page.HasValue ? pageSize : null;

        bool descending = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        (int totalCount, IReadOnlyList<ProductWorkRate> rates) = await _repository.GetPagedByProductAsync(
            request.Status,
            request.Search,
            skipProducts,
            takeProducts,
            request.SortBy,
            descending,
            cancellationToken);

        GetAllProductWorkRatesResponse response = new()
        {
            Items = rates.Select(Map).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };

        if (request.IncludeCounts)
        {
            // Counted without the status filter: these are the numbers the filter itself offers.
            (int active, int inactive, int all) = await _repository.GetGroupCountsAsync(
                request.Search,
                cancellationToken);

            response = new GetAllProductWorkRatesResponse
            {
                Items = response.Items,
                TotalCount = response.TotalCount,
                Page = response.Page,
                PageSize = response.PageSize,
                StatusCounts = new ProductWorkRateGroupCounts
                {
                    Active = active,
                    Inactive = inactive,
                    All = all,
                },
            };
        }

        return response;
    }

    private static ProductWorkRateDto Map(ProductWorkRate rate)
    {
        return new ProductWorkRateDto
        {
            Id = rate.Id,
            ProductId = rate.ProductId,
            ProductName = rate.Product.Name,
            ProductSku = rate.Product.Sku,
            WorkRateId = rate.WorkRateId,
            AssemblyRatePerDay = rate.AssemblyRatePerDay,
            ValidFrom = rate.ValidFrom,
            Status = rate.Status,
            RemovedFrom = rate.RemovedFrom,
            CreatedAt = rate.CreatedAt,
            UpdatedAt = rate.UpdatedAt,
        };
    }
}
