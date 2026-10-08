using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;

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
}
