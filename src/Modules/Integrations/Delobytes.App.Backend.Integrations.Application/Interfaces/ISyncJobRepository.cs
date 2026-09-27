using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for SyncJob aggregate.
/// </summary>
public interface ISyncJobRepository
{
    /// <summary>
    /// Finds a sync job by primary key.
    /// </summary>
    /// <param name="id">Sync job identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SyncJob entity or null.</returns>
    Task<SyncJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a sync job by primary key with related Connection and Channel loaded.
    /// </summary>
    /// <param name="id">Sync job identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>SyncJob entity with Connection and Channel or null.</returns>
    Task<SyncJob?> FindByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a new sync job.
    /// </summary>
    /// <param name="syncJob">Sync job entity.</param>
    void Add(SyncJob syncJob);

    /// <summary>
    /// Updates an existing sync job.
    /// </summary>
    /// <param name="syncJob">Sync job entity.</param>
    void Update(SyncJob syncJob);

    /// <summary>
    /// Returns all products-import sync jobs for the current tenant, ordered by creation date descending.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<List<SyncJob>> GetProductsImportJobsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when there is already a Pending or Running ProductsImport job for the given connection.
    /// Used to prevent duplicate import jobs.
    /// </summary>
    /// <param name="connectionId">Connection identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasActivePendingOrRunningJobForConnectionAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of state entries written to the database.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
