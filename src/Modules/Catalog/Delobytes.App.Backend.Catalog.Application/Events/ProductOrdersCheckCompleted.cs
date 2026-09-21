using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Events;

/// <summary>
/// Published by the Sales-side handler after checking Orders existence.
/// Catalog-side handler completes or cancels product deletion based on this.
/// </summary>
public record ProductOrdersCheckCompleted : INotification
{
    public Guid ProductId { get; init; }

    public bool HasOrders { get; init; }

    public int OrderCount { get; init; }

    public DateTimeOffset CheckedAt { get; init; }
}
