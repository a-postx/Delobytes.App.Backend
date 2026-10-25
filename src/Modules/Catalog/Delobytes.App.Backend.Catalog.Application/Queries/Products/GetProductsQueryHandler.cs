using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

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

public class GetProductsResponse
{
    /// <summary>
    /// Rows of the requested page, or the full list when pagination was not requested.
    /// </summary>
    public List<ProductItem> Items { get; set; } = new();

    /// <summary>
    /// Total number of products matching the status filter, ignoring pagination.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Page that was actually served (1-based). 1 when pagination was not requested.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size that was actually applied, after clamping. Equal to <see cref="TotalCount"/>
    /// when pagination was not requested.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Per-status totals, present only when the request asked for counts. Lets the client
    /// render tab counters for a paged list in a single round trip.
    /// </summary>
    public ProductStatusCounts? StatusCounts { get; set; }
}

/// <summary>
/// Per-status product totals, independent of the active status filter.
/// </summary>
public class ProductStatusCounts
{
    public int Active { get; set; }

    public int Archived { get; set; }

    /// <summary>Total across every status.</summary>
    public int All { get; set; }
}

public class ProductItem
{
    public Guid Id { get; set; }

    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public ProductStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // Placeholder for future marketplace import source — manual entry for now
    public string CreationSource { get; set; } = "Manual";

    public List<Products.ProductBarcodeDto>? Barcodes { get; set; }

    // The list view does not show dimensions; the single-product endpoint does.
    public Products.PackingUnitDto? PackingUnit { get; set; }

    // The list view returns only thumbnail photos; the single-product endpoint returns all variants.
    public List<Products.ProductPhotoDto>? Photos { get; set; }

    /// <summary>
    /// Links of this product to its external cards on sales channels (ChannelProduct).
    /// Empty/null means the product has no marketplace connections and is fully editable.
    /// </summary>
    public List<Products.ProductChannelLinkDto>? ChannelLinks { get; set; }

    /// <summary>
    /// True when the product has at least one active work rate. Populated only when the request
    /// sets IncludeWorkRateCoverage; otherwise it stays false for every item.
    /// </summary>
    public bool HasActiveWorkRate { get; set; }
}

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, GetProductsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MinPageSize = 1;

    private readonly IProductRepository _repository;
    private readonly IProductPhotoService _photoService;

    public GetProductsQueryHandler(IProductRepository repository, IProductPhotoService photoService)
    {
        _repository = repository;
        _photoService = photoService;
    }

    public async Task<GetProductsResponse> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        // No page requested means "give me everything" — the legacy contract that the work-rate
        // and channel-cost views still rely on. Paging is applied only when a page number arrives.
        int page = Math.Max(request.Page ?? 1, 1);
        int pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        int? skip = request.Page.HasValue ? (page - 1) * pageSize : null;
        int? take = request.Page.HasValue ? pageSize : null;

        bool descending = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        (int totalCount, IReadOnlyList<Product> products) = await _repository.GetPagedAsync(
            request.Status,
            skip,
            take,
            request.SortBy,
            descending,
            cancellationToken,
            request.Search,
            request.IncludeWorkRateCoverage);

        GetProductsResponse response = new()
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = products.Select(p => new ProductItem
            {
                Id = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                Description = p.Description,
                Status = p.Status,
                HasActiveWorkRate = request.IncludeWorkRateCoverage
                    && p.ProductWorkRates.Any(r => r.Status == ProductWorkRateStatus.Active),
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                ArchivedAt = p.ArchivedAt,
                DeletionRequestedAt = p.DeletionRequestedAt,
                DeletedAt = p.DeletedAt,
                Barcodes = p.Barcodes
                    .Select(b => new ProductBarcodeDto
                    {
                        Id = b.Id,
                        Value = b.Value,
                        Type = b.Type,
                        IsDefault = b.IsDefault,
                    })
                    .ToList(),

                // The list view does not show dimensions; the single-product endpoint does.
                PackingUnit = null,

                // The list view returns only thumbnail photos; the single-product endpoint returns all variants.
                Photos = p.Photos
                    .Where(ph => ph.Status == ProductPhotoStatus.Uploaded && ph.SizeVariant == "thumbnail")
                    .OrderBy(ph => ph.DisplayOrder)
                    .Select(ph => new ProductPhotoDto
                    {
                        Id = ph.Id,
                        DisplayOrder = ph.DisplayOrder,
                        SizeVariant = ph.SizeVariant,
                        Url = _photoService.GetPublicUrl(ph),
                        Width = ph.Width,
                        Height = ph.Height
                    })
                    .ToList(),
                ChannelLinks = p.ChannelProducts
                    .Select(cp => new ProductChannelLinkDto
                    {
                        ChannelId = cp.ChannelId,
                        ChannelName = cp.Channel.Name,
                        ChannelCode = cp.Channel.Code,
                        ExternalProductId = cp.ExternalProductId,
                        ExternalSku = cp.ExternalSku,
                        IsActive = cp.IsActive,
                    })
                    .ToList(),
            }).ToList(),
        };

        if (request.IncludeCounts)
        {
            (int active, int archived, int all) = await _repository.GetStatusCountsAsync(cancellationToken, request.Search);
            response.StatusCounts = new ProductStatusCounts
            {
                Active = active,
                Archived = archived,
                All = all,
            };
        }

        return response;
    }
}
