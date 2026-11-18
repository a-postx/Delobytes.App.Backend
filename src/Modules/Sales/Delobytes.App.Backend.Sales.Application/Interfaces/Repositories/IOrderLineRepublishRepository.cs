using Delobytes.App.Backend.Sales.Application.Models;

namespace Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;

/// <summary>
/// Keyset-paged source of order lines for the re-publish command. The re-publish exists both to
/// backfill existing orders and to recover from an event lost between Sales and Finance, so it must
/// page deterministically and stay bounded per request on a single-CPU host.
/// </summary>
public interface IOrderLineRepublishRepository
{
    /// <summary>
    /// Returns one page of order lines ordered by their identifier, strictly after
    /// <paramref name="afterToken"/>. The tenant is the one resolved for the current scope; the
    /// implementation must not read across tenants.
    /// </summary>
    /// <param name="channelId">Optional channel filter.</param>
    /// <param name="fromUtc">Optional inclusive lower bound of the order date.</param>
    /// <param name="toUtcExclusive">Optional exclusive upper bound of the order date.</param>
    /// <param name="afterToken">Last identifier of the previous page, or null for the first page.</param>
    /// <param name="limit">Maximum number of rows.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The page and, implicitly through the rows, the next continuation token.</returns>
    public Task<IReadOnlyList<RepublishOrderLine>> GetPageAsync(
        Guid? channelId,
        DateTimeOffset? fromUtc,
        DateTimeOffset? toUtcExclusive,
        string? afterToken,
        int limit,
        CancellationToken cancellationToken);
}
