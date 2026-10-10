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
    void Add(RawApiResponse rawApiResponse);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Marks every not-yet-processed raw response of the given SyncJob as processed.
    /// Called by a consumer right after it has inspected the mapped result of an API call and
    /// decided what to do with it, so <c>ProcessedAt</c> distinguishes "received over the wire"
    /// (set by the capturing HTTP handler) from "looked at by business logic" (set here) — rows
    /// left with <c>ProcessedAt == null</c> long after <c>ReceivedAt</c> indicate the consumer
    /// crashed or hung before finishing with that response.
    /// </summary>
    /// <param name="syncJobId">SyncJob identifier whose pending raw responses should be closed out.</param>
    /// <param name="processedAt">Timestamp to record as the processing time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task MarkPendingAsProcessedAsync(Guid syncJobId, DateTimeOffset processedAt, CancellationToken cancellationToken);
}
