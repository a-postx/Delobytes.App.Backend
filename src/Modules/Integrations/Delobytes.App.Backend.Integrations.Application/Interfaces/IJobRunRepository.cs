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
}
