using Delobytes.App.Backend.Sales.Application.Models;

namespace Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;

/// <summary>
/// Read-only access to order-line facts for aggregated reporting. Returns flat, already filtered
/// rows; aggregation and bucketing are the caller's job.
/// </summary>
public interface IOrderStatsRepository
{
    /// <summary>
    /// Returns the surviving order-line facts for the given UTC window, already filtered by
    /// currency and cancellation, ready for in-memory bucketing by the caller.
    /// </summary>
    /// <param name="fromUtc">Inclusive lower bound of the order date window, in UTC.</param>
    /// <param name="toUtcExclusive">Exclusive upper bound of the order date window, in UTC.</param>
    /// <param name="currency">Tenant currency; both the order and the line must match it.</param>
    /// <param name="channelId">Optional channel filter. Null means all channels.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>One fact per surviving order line. Empty when nothing matches.</returns>
    public Task<IReadOnlyList<OrderLineFact>> GetLineFactsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtcExclusive,
        string currency,
        Guid? channelId,
        CancellationToken cancellationToken);
}
