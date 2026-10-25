namespace Delobytes.App.Backend.Catalog.Contracts.Events;

/// <summary>
/// Published by Catalog when a product's deletion is requested and the product has at least one
/// linked ChannelProduct. Consumed by Sales, which checks whether Orders exist for the given
/// ChannelProduct IDs and reports the outcome back via
/// <c>Delobytes.App.Backend.Sales.Contracts.Events.ProductOrdersCheckCompletedEvent</c>.
/// </summary>
public record ProductDeletionRequestedEvent
{
    /// <summary>Identifier of the product whose deletion was requested, in Catalog.</summary>
    public Guid ProductId { get; init; }

    /// <summary>ChannelProduct IDs linked to this product at the time of the request.</summary>
    public List<Guid> ChannelProductIds { get; init; } = new();

    public Guid TenantId { get; init; }

    public DateTimeOffset RequestedAt { get; init; }
}
