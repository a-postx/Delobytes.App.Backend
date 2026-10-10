namespace Delobytes.App.Backend.Catalog.Contracts.Events;

/// <summary>
/// Published by Catalog after a <c>ChannelProduct</c> link has been created, re-activated or
/// updated and its transaction has committed.
/// Consumed by the Sales module to maintain its <c>ChannelProductRef</c> projection, which resolves
/// order lines to catalogue products without a cross-module call. The handler upserts by
/// <see cref="ChannelProductId"/>, so repeated publication of the same link is harmless.
/// </summary>
public record ChannelProductLinkedEvent
{
    /// <summary>
    /// Identifier of the ChannelProduct link; the upsert key of the Sales-side projection.
    /// </summary>
    public Guid ChannelProductId { get; init; }

    /// <summary>
    /// Identifier of the linked product in Catalog.
    /// </summary>
    public Guid ProductId { get; init; }

    /// <summary>
    /// Identifier of the Channel in Catalog.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// External product identifier in the marketplace, e.g. the Wildberries nmID.
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
    /// Publicly reachable URL of the product's primary photo, if one has been imported.
    /// </summary>
    public string? PhotoUrl { get; init; }
}
