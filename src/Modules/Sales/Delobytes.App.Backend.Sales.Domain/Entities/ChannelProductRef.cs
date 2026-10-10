using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Sales.Domain.Entities;

/// <summary>
/// Sales-side projection of a catalogue channel product, maintained from Catalog integration events.
/// </summary>
/// <remarks>
/// <see cref="OrderLine.ChannelProductId"/> cannot be resolved from inside Sales: the
/// <c>(ChannelId, ExternalProductId) → ChannelProductId</c> mapping lives in another module and
/// another database, and synchronous cross-module calls are forbidden. This projection is the
/// local copy that makes resolution a lookup instead of a call.
/// </remarks>
public class ChannelProductRef : ITenantScoped
{
    /// <summary>
    /// Gets or sets the channel product identifier. Primary key — mirrors
    /// <c>Catalog.ChannelProduct.Id</c>.
    /// </summary>
    public Guid ChannelProductId { get; set; }

    /// <summary>
    /// Gets or sets the catalogue product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side product identity. Part of the natural key used for resolution.
    /// </summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the channel-side SKU, where the channel distinguishes it from
    /// <see cref="ExternalProductId"/>.
    /// </summary>
    public string? ExternalSku { get; set; }

    /// <summary>
    /// Gets or sets the seller's SKU (<c>Catalog.Product.Sku</c>).
    /// </summary>
    public string? Sku { get; set; }

    /// <summary>
    /// Gets or sets the product name snapshot, for display without a cross-module call.
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// Gets or sets the product photo URL snapshot.
    /// </summary>
    public string? PhotoUrl { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the link is still active in the catalogue.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the time of the last projection update.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }
}
