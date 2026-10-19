using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for OrdersBackfillState (per-connection day-by-day orders backfill cursor).
/// </summary>
public interface IOrdersBackfillStateRepository
{
    /// <summary>
    /// Finds the backfill state for the given connection.
    /// </summary>
    public Task<OrdersBackfillState?> GetAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the state if none exists for its ConnectionId, otherwise updates the existing one.
    /// </summary>
    public Task UpsertAsync(OrdersBackfillState state, CancellationToken cancellationToken);
}
