namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// Link between a product and its external card on a sales channel (ChannelProduct), as
/// exposed through the API. Shared by the product list and single-product queries.
///
/// Everything here is channel-owned and read-only: the values describe what the marketplace
/// reports about the card and are refreshed by every import. Product fields (name, description,
/// SKU, canonical packing unit) are the catalogue's own and stay canonical; a channel value never
/// promotes itself into them.
/// </summary>
public class ProductChannelLinkDto
{
    public Guid ChannelId { get; set; }

    public string ChannelName { get; set; } = default!;

    /// <summary>System channel template code ("wildberries", "ozon", "yandex.kit"), or null for a custom channel.</summary>
    public string? ChannelCode { get; set; }

    /// <summary>External product identifier in the marketplace/channel system (e.g. nmID for WB).</summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// The seller's own article for this card as the channel reports it (<c>vendorCode</c> for
    /// Wildberries, <c>offer_id</c> for Ozon). It is not a mirror of the internal SKU.
    /// </summary>
    public string? ExternalSku { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the card was present in the channel listing at the
    /// last import — a channel-reported fact, not the user's decision to hide the product. That
    /// decision is <c>ProductItem.Status</c>.
    /// </summary>
    public bool IsActive { get; set; }
}
