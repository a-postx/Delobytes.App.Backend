using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for OrdersBackfillState (per-connection, per-job-type orders backfill cursor).
/// </summary>
public interface IOrdersBackfillStateRepository
{
    /// <summary>
    /// Finds the backfill state for the given connection and orders job type.
    /// </summary>
    public Task<OrdersBackfillState?> GetAsync(
        Guid connectionId, JobType jobType, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the state if none exists for its (ConnectionId, JobType) pair, otherwise updates the
    /// existing one.
    /// </summary>
    public Task UpsertAsync(OrdersBackfillState state, CancellationToken cancellationToken);
}
