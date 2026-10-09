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
    Task<SyncJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a sync job by primary key with related Connection and Channel loaded.
    /// </summary>
    Task<SyncJob?> FindByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken);

    void Add(SyncJob syncJob);

    void Update(SyncJob syncJob);

    /// <summary>
    /// Returns all products-import sync jobs for the current tenant, ordered by creation date descending.
    /// </summary>
    Task<List<SyncJob>> GetProductsImportJobsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when there is already a Pending or Running ProductsImport job for the given connection.
    /// </summary>
    Task<bool> HasActivePendingOrRunningJobForConnectionAsync(Guid connectionId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when a batch result with the given MessageId already exists (idempotency check).
    /// </summary>
    Task<bool> BatchResultExistsAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a batch result record.
    /// </summary>
    void AddBatchResult(SyncJobBatchResult batchResult);

    /// <summary>
    /// Returns aggregated counters across all stored batch results for the given SyncJob.
    /// </summary>
    Task<SyncJobBatchResultTotals> GetBatchResultTotalsAsync(Guid syncJobId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of batch result rows recorded so far for the given SyncJob.
    /// Used by the aggregator to decide whether every expected batch (see
    /// <see cref="SyncJob.TotalImportBatches"/>) has already been reported.
    /// </summary>
    Task<int> CountBatchResultsAsync(Guid syncJobId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
