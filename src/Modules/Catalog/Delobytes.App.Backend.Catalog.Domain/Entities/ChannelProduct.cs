using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents the link between a product and a sales channel with channel-specific data.
/// </summary>
public class ChannelProduct : ITenantScoped
{
    /// <summary>
    /// Gets or sets the channel product unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the external product identifier in the marketplace/channel system.
    /// </summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the seller's own article for this card as the channel reports it
    /// (<c>vendorCode</c> for Wildberries, <c>offer_id</c> for Ozon). It is the channel's copy, not
    /// a mirror of <see cref="Product.Sku"/>: the import writes it unconditionally on every sync,
    /// while the internal SKU is user-editable and independent.
    /// </summary>
    public string? ExternalSku { get; set; }

    /// <summary>
    /// Gets or sets the product name as the channel reports it. Channel-owned data: refreshed on
    /// every import and read-only in the UI. The canonical name is <see cref="Product.Name"/>.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the product description as the channel reports it. Channel-owned data:
    /// refreshed on every import and read-only in the UI. The canonical description is
    /// <see cref="Product.Description"/>.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets channel-specific data as JSON string.
    /// </summary>
    public string? ChannelSpecificData { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the card is still present in the channel's
    /// listing, as the last import observed it. Technical, channel-owned state: it is written only
    /// by the import and never reflects a user decision, which lives in <see cref="Product.Status"/>.
    /// </summary>
    /// <remarks>
    /// Not a product visibility flag. Archiving a product deliberately leaves this alone: the card
    /// remains listed in the channel after the user hides the product in the catalogue, and
    /// deactivating it here would conflate the two.
    /// </remarks>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the product was last synced with the channel.
    /// </summary>
    public DateTimeOffset? LastSyncedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the channel product was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the channel product was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: the product.
    /// </summary>
    public Product Product { get; set; } = default!;

    /// <summary>
    /// Navigation property: the channel.
    /// </summary>
    public Channel Channel { get; set; } = default!;
}
