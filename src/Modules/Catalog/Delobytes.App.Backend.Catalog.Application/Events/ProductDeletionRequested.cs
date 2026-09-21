using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Events;

/// <summary>
/// Published when product deletion is requested.
/// Sales module handler checks whether Orders exist for this product's ChannelProducts.
/// </summary>
public record ProductDeletionRequested : INotification
{
    public Guid ProductId { get; init; }

    /// <summary>ChannelProduct IDs linked to this product at the time of the request.</summary>
    public List<Guid> ChannelProductIds { get; init; } = new();

    public Guid TenantId { get; init; }

    public DateTimeOffset RequestedAt { get; init; }
}
