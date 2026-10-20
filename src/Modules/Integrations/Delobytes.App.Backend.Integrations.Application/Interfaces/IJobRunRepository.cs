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
    public Task<JobRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new job run and persists it.
    /// </summary>
    public Task<JobRun> CreateAsync(JobRun jobRun, CancellationToken cancellationToken);

    /// <summary>
    /// Persists changes made to an existing job run.
    /// </summary>
    public Task UpdateAsync(JobRun jobRun, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a filtered, optionally paged set of job runs for the current tenant plus the
    /// total number of matching rows (counted before paging).
    /// </summary>
    /// <param name="jobType">Optional job type filter; null means "all types".</param>
    /// <param name="connectionId">Optional connection filter; null means "all connections".</param>
    /// <param name="statuses">Optional status filter; null or empty means "all statuses".</param>
    /// <param name="skip">Rows to skip, or null for no paging.</param>
    /// <param name="take">Page size, or null for no paging.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<(int TotalCount, List<JobRun> Items)> GetPagedAsync(
        JobType? jobType,
        Guid? connectionId,
        JobStatus[]? statuses,
        int? skip,
        int? take,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when a Pending or Running job run already exists for the given
    /// connection and job type (used to enforce one active job per connection/type).
    /// </summary>
    public Task<bool> HasActiveJobAsync(Guid connectionId, JobType jobType, CancellationToken cancellationToken);

    /// <summary>
    /// Returns up to <paramref name="limit"/> Pending job runs for the given tenant,
    /// ordered by creation date ascending, for the worker to pick up.
    /// </summary>
    public Task<List<JobRun>> GetPendingJobsAsync(Guid tenantId, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of Running job runs for the given tenant, used by the worker to
    /// compute how many additional Pending job runs fit within the tenant's configured
    /// parallelism limit.
    /// </summary>
    public Task<int> CountRunningJobsAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns every Running job run (across all tenants) started before
    /// <paramref name="threshold"/>, for the watchdog to reconcile stuck jobs.
    /// </summary>
    public Task<List<JobRun>> GetRunningJobsOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the type-specific ProductsImport data for the given job run.
    /// </summary>
    public Task<ProductsImportJob?> GetProductsImportJobAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Finds the type-specific OrdersSync data for the given job run.
    /// </summary>
    public Task<OrdersImportJob?> GetOrdersImportJobAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the type-specific ProductsImport row linked to an existing JobRun and persists it.
    /// </summary>
    public Task CreateProductsImportJobAsync(ProductsImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Creates the type-specific OrdersSync row linked to an existing JobRun and persists it.
    /// </summary>
    public Task CreateOrdersImportJobAsync(OrdersImportJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Finds a job run by primary key with the related Connection (and its SystemChannelTemplate)
    /// loaded, for consumers that need to call out to the channel's API client.
    /// </summary>
    public Task<JobRun?> GetByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Reads the current Status directly from the database, bypassing any tracked/cached
    /// instance of the job run. Used by long-running consumers to detect a cancellation
    /// requested concurrently while a page/batch is in flight.
    /// </summary>
    public Task<JobStatus> GetStatusAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Returns true when a batch result with the given MessageId already exists (idempotency check).
    /// </summary>
    public Task<bool> BatchResultExistsAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds a batch result record.
    /// </summary>
    public void AddBatchResult(JobRunBatchResult batchResult);

    /// <summary>
    /// Returns aggregated counters across all stored batch results for the given JobRun.
    /// </summary>
    public Task<JobRunBatchResultTotals> GetBatchResultTotalsAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the number of batch result rows recorded so far for the given JobRun.
    /// Used by the aggregator to decide whether every expected batch (see
    /// <see cref="ProductsImportJob.TotalImportBatches"/>) has already been reported.
    /// </summary>
    public Task<int> CountBatchResultsAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Persists all pending changes.
    /// </summary>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>
/// Aggregated counters across all batch results for a single JobRun.
/// </summary>
public sealed class JobRunBatchResultTotals
{
    public int TotalProcessed { get; init; }
    public int TotalCreated { get; init; }
    public int TotalUpdated { get; init; }
    public int TotalSkipped { get; init; }
    public int TotalFailed { get; init; }
    public string? CombinedErrors { get; init; }
}
