namespace Delobytes.App.Backend.Sales.Contracts.Events;

/// <summary>
/// Published by Sales after checking whether Orders exist for the ChannelProduct IDs carried by
/// a <c>ProductDeletionRequestedEvent</c>. Consumed by Catalog to complete or cancel the
/// product's pending deletion.
/// </summary>
public record ProductOrdersCheckCompletedEvent
{
    /// <summary>Identifier of the product whose deletion is being evaluated, in Catalog.</summary>
    public Guid ProductId { get; init; }

    /// <summary>True when at least one Order references one of the requested ChannelProducts.</summary>
    public bool HasOrders { get; init; }

    /// <summary>Number of distinct Orders found. Zero when <see cref="HasOrders"/> is false.</summary>
    public int OrderCount { get; init; }

    /// <summary>Instant the check completed, as reported by Sales.</summary>
    public DateTimeOffset CheckedAt { get; init; }
}
