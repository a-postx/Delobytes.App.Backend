using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Persistence port of the production costs resolved by Catalog.
/// </summary>
public interface IOrderLineCostStore
{
    /// <summary>
    /// Loads the costs of <paramref name="orderLineIds"/>.
    /// </summary>
    /// <param name="orderLineIds">Order line identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The costs found.</returns>
    public Task<IReadOnlyList<OrderLineCost>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stores <paramref name="cost"/> only when it is newer than the stored one. The comparison must
    /// be part of the write, not a read followed by a blind write: two consumers can resolve the same
    /// line concurrently, and a row version alone does not order them. An implementation is free to
    /// retry a lost insert race, but it must never overwrite an equal or newer <c>ResolvedAt</c>.
    /// </summary>
    /// <param name="cost">The incoming cost result.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True when the incoming result was written; false when an equal or newer one exists.</returns>
    public Task<bool> TryUpsertNewerAsync(OrderLineCost cost, CancellationToken cancellationToken);
}
