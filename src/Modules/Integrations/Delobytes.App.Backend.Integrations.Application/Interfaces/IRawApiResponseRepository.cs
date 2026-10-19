using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for RawApiResponse aggregate.
/// </summary>
public interface IRawApiResponseRepository
{
    /// <summary>
    /// Adds a new raw API response.
    /// </summary>
    /// <param name="rawApiResponse">Raw API response entity.</param>
    public void Add(RawApiResponse rawApiResponse);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes every raw API response received before <paramref name="threshold"/>, across all
    /// tenants.
    /// </summary>
    /// <param name="threshold">Exclusive upper bound on ReceivedAt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of deleted rows.</returns>
    public Task<int> DeleteOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken);
}
