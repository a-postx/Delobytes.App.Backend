namespace Delobytes.App.Backend.Catalog.Contracts.Events;

/// <summary>
/// Published by Catalog when a <c>ChannelProduct</c> link is removed or deactivated.
/// Consumed by the Sales module to deactivate or drop the matching <c>ChannelProductRef</c>.
/// The external identity is carried so the consumer can still match the projection when the link
/// row is deleted rather than deactivated.
/// </summary>
public record ChannelProductUnlinkedEvent
{
    /// <summary>
    /// Identifier of the removed or deactivated ChannelProduct link.
    /// </summary>
    public Guid ChannelProductId { get; init; }

    /// <summary>
    /// Identifier of the product the link pointed at.
    /// </summary>
    public Guid ProductId { get; init; }

    /// <summary>
    /// Identifier of the Channel the link pointed at.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// External product identifier in the marketplace.
    /// </summary>
    public string ExternalProductId { get; init; } = default!;

    /// <summary>
    /// External SKU reported by the marketplace when it differs from the internal SKU.
    /// </summary>
    public string? ExternalSku { get; init; }

    /// <summary>
    /// Internal product SKU (<c>Product.Sku</c>).
    /// </summary>
    public string? Sku { get; init; }

    /// <summary>
    /// Product name at the time of publication.
    /// </summary>
    public string? ProductName { get; init; }

    /// <summary>
    /// Publicly reachable URL of the product's primary photo, if one had been imported.
    /// </summary>
    public string? PhotoUrl { get; init; }
}
