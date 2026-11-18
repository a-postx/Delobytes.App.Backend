using Delobytes.App.Backend.Finance.Application.Models;
using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Persistence port of the Finance-side projection of Sales order lines.
/// </summary>
public interface IOrderLineRefStore
{
    /// <summary>
    /// Loads the projections of <paramref name="orderLineIds"/>.
    /// </summary>
    /// <param name="orderLineIds">Order line identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projections found.</returns>
    public Task<IReadOnlyList<OrderLineRef>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the projections matching the attribution keys of a set of journal entries.
    /// </summary>
    /// <param name="keys">Attribution keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The projections found.</returns>
    public Task<IReadOnlyList<OrderLineRef>> LoadByAttributionAsync(
        IReadOnlyCollection<OrderLineAttributionKey> keys,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates the projections and saves them, keyed by the order line identifier.
    /// </summary>
    /// <param name="refs">Projections to write.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task UpsertAsync(IReadOnlyCollection<OrderLineRef> refs, CancellationToken cancellationToken);
}
