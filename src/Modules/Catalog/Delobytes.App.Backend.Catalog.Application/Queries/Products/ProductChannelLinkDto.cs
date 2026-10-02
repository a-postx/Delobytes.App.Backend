namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// Link between a product and its external card on a sales channel (ChannelProduct), as
/// exposed through the API. Shared by the product list and single-product queries.
///
/// Temporary measure (see refactor TODO): while marketplace write-back is not implemented,
/// import is the source of truth for Name/Description/Barcodes/PackingUnit of a linked
/// product and will overwrite local edits. The backend does not reject edits to those
/// fields in UpdateProductCommand — the frontend disables them whenever ChannelLinks is
/// non-empty. Once write-back exists, import should stop overwriting these fields and this
/// frontend-only restriction can be lifted.
/// </summary>
public class ProductChannelLinkDto
{
    public Guid ChannelId { get; set; }

    public string ChannelName { get; set; } = default!;

    /// <summary>System channel template code ("wildberries", "ozon", "yandex.kit"), or null for a custom channel.</summary>
    public string? ChannelCode { get; set; }

    /// <summary>External product identifier in the marketplace/channel system (e.g. nmID for WB).</summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>External SKU if it differs from Product.Sku (e.g. vendorCode for WB).</summary>
    public string? ExternalSku { get; set; }

    public bool IsActive { get; set; }
}
