using Delobytes.App.Backend.Identity.Domain.Interfaces;

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
    /// Gets or sets the external SKU if it differs from Product.Sku.
    /// </summary>
    public string? ExternalSku { get; set; }

    /// <summary>
    /// Gets or sets channel-specific data as JSON string.
    /// </summary>
    public string? ChannelSpecificData { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the channel product link is active.
    /// </summary>
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
