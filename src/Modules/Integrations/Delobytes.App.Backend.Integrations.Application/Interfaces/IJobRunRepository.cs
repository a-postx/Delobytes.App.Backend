using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for the JobRun aggregate (common execution metadata shared by
/// every background job type).
/// </summary>
public interface IJobRunRepository
{
    /// <summary>
    /// Finds a job run by primary key.
    /// </summary>
    Task<JobRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new job run and persists it.
    /// </summary>
    Task<JobRun> CreateAsync(JobRun jobRun, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes made to an existing job run.
    /// </summary>
    Task UpdateAsync(JobRun jobRun, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when a Pending or Running job run already exists for the given
    /// connection and job type (used to enforce one active job per connection/type).
    /// </summary>
    Task<bool> HasActiveJobAsync(Guid connectionId, JobType jobType, CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <paramref name="limit"/> Pending job runs for the given tenant,
    /// ordered by creation date ascending, for the worker to pick up.
    /// </summary>
    Task<List<JobRun>> GetPendingJobsAsync(Guid tenantId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of Running job runs for the given tenant, used by the worker to
    /// compute how many additional Pending job runs fit within the tenant's configured
    /// parallelism limit.
    /// </summary>
    Task<int> CountRunningJobsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every Running job run (across all tenants) started before
    /// <paramref name="threshold"/>, for the watchdog to reconcile stuck jobs.
    /// </summary>
    Task<List<JobRun>> GetRunningJobsOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the type-specific ProductsImport data for the given job run.
    /// </summary>
    Task<ProductsImportJob?> GetProductsImportJobAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the type-specific OrdersSync data for the given job run.
    /// </summary>
    Task<OrdersImportJob?> GetOrdersImportJobAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the type-specific ProductsImport row linked to an existing JobRun and persists it.
    /// </summary>
    Task CreateProductsImportJobAsync(ProductsImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the type-specific OrdersSync row linked to an existing JobRun and persists it.
    /// </summary>
    Task CreateOrdersImportJobAsync(OrdersImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a job run by primary key with the related Connection (and its SystemChannelTemplate)
    /// loaded, for consumers that need to call out to the channel's API client.
    /// </summary>
    Task<JobRun?> GetByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current Status directly from the database, bypassing any tracked/cached
    /// instance of the job run. Used by long-running consumers to detect a cancellation
    /// requested concurrently while a page/batch is in flight.
    /// </summary>
    Task<JobStatus> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when a batch result with the given MessageId already exists (idempotency check).
    /// </summary>
    Task<bool> BatchResultExistsAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a batch result record.
    /// </summary>
    void AddBatchResult(JobRunBatchResult batchResult);

    /// <summary>
    /// Returns aggregated counters across all stored batch results for the given JobRun.
    /// </summary>
    Task<JobRunBatchResultTotals> GetBatchResultTotalsAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of batch result rows recorded so far for the given JobRun.
    /// Used by the aggregator to decide whether every expected batch (see
    /// <see cref="ProductsImportJob.TotalImportBatches"/>) has already been reported.
    /// </summary>
    Task<int> CountBatchResultsAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
