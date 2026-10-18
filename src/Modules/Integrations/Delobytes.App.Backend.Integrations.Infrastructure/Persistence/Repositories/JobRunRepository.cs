using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IJobRunRepository.
/// </summary>
public class JobRunRepository : IJobRunRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobRunRepository"/> class.
    /// </summary>
    public JobRunRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<JobRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.JobRuns.FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<JobRun> CreateAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        _context.JobRuns.Add(jobRun);
        await _context.SaveChangesAsync(cancellationToken);
        return jobRun;
    }

    /// <inheritdoc/>
    public Task UpdateAsync(JobRun jobRun, CancellationToken cancellationToken)
    {
        _context.JobRuns.Update(jobRun);
        return _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<(int TotalCount, List<JobRun> Items)> GetPagedAsync(
        JobType? jobType,
        Guid? connectionId,
        JobStatus[]? statuses,
        int? skip,
        int? take,
        CancellationToken cancellationToken)
    {
        IQueryable<JobRun> query = _context.JobRuns.AsNoTracking();

        if (jobType.HasValue)
        {
            query = query.Where(j => j.JobType == jobType.Value);
        }

        if (connectionId.HasValue)
        {
            query = query.Where(j => j.ConnectionId == connectionId.Value);
        }

        if (statuses is { Length: > 0 })
        {
            // Materialised to a List because an array captured in the expression tree is not
            // translated reliably against the int-mapped enum column.
            List<JobStatus> statusList = statuses.ToList();
            query = query.Where(j => statusList.Contains(j.Status));
        }

        int totalCount = await query.CountAsync(cancellationToken);

        query = query
            .OrderByDescending(j => j.CreatedAt)
            .ThenByDescending(j => j.Id);

        if (skip.HasValue)
        {
            query = query.Skip(skip.Value);
        }

        if (take.HasValue)
        {
            query = query.Take(take.Value);
        }

        List<JobRun> items = await query.ToListAsync(cancellationToken);

        return (totalCount, items);
    }

    /// <inheritdoc/>
    public Task<bool> HasActiveJobAsync(Guid connectionId, JobType jobType, CancellationToken cancellationToken)
    {
        return _context.JobRuns.AnyAsync(
            j => j.ConnectionId == connectionId
                && j.JobType == jobType
                && (j.Status == JobStatus.Pending || j.Status == JobStatus.Running),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<JobRun>> GetPendingJobsAsync(Guid tenantId, int limit, CancellationToken cancellationToken)
    {
        // The current ITenantContext already restricts the query via the global query filter;
        // the explicit TenantId comparison guards against a mismatched ambient context.
        return _context.JobRuns
            .Where(j => EF.Property<Guid>(j, "TenantId") == tenantId && j.Status == JobStatus.Pending)
            .OrderBy(j => j.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<int> CountRunningJobsAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        return _context.JobRuns
            .Where(j => EF.Property<Guid>(j, "TenantId") == tenantId && j.Status == JobStatus.Running)
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<List<JobRun>> GetRunningJobsOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        // Watchdog must see every tenant's stuck jobs, not just the caller's current tenant.
        return _context.JobRuns
            .IgnoreQueryFilters()
            .Where(j => j.Status == JobStatus.Running && j.StartedAt != null && j.StartedAt < threshold)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<ProductsImportJob?> GetProductsImportJobAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return _context.ProductsImportJobs.FirstOrDefaultAsync(p => p.JobRunId == jobRunId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<OrdersImportJob?> GetOrdersImportJobAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return _context.OrdersImportJobs.FirstOrDefaultAsync(o => o.JobRunId == jobRunId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CreateProductsImportJobAsync(ProductsImportJob job, CancellationToken cancellationToken)
    {
        _context.ProductsImportJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task CreateOrdersImportJobAsync(OrdersImportJob job, CancellationToken cancellationToken)
    {
        _context.OrdersImportJobs.Add(job);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<JobRun?> GetByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.JobRuns
            .Include(j => j.Connection)
            .ThenInclude(c => c.SystemChannelTemplate)
            .FirstOrDefaultAsync(j => j.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<JobStatus> GetStatusAsync(Guid id, CancellationToken cancellationToken)
    {
        // AsNoTracking bypasses the change tracker's identity map, so this always reflects
        // the database rather than an already-loaded (and possibly stale) tracked instance.
        return _context.JobRuns
            .AsNoTracking()
            .Where(j => j.Id == id)
            .Select(j => j.Status)
            .FirstAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> BatchResultExistsAsync(Guid messageId, CancellationToken cancellationToken)
    {
        return _context.JobRunBatchResults.AnyAsync(r => r.MessageId == messageId, cancellationToken);
    }

    /// <inheritdoc/>
    public void AddBatchResult(JobRunBatchResult batchResult)
    {
        _context.JobRunBatchResults.Add(batchResult);
    }

    /// <inheritdoc/>
    public async Task<JobRunBatchResultTotals> GetBatchResultTotalsAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        List<string?> errors = await _context.JobRunBatchResults
            .Where(r => r.JobRunId == jobRunId && r.ErrorMessage != null)
            .Select(r => r.ErrorMessage)
            .ToListAsync(cancellationToken);

        // Aggregate in a single query to avoid N+1.
        JobRunBatchResultTotals totals = await _context.JobRunBatchResults
            .Where(r => r.JobRunId == jobRunId)
            .GroupBy(r => r.JobRunId)
            .Select(g => new JobRunBatchResultTotals
            {
                TotalProcessed = g.Sum(r => r.RecordsProcessed),
                TotalCreated = g.Sum(r => r.RecordsCreated),
                TotalUpdated = g.Sum(r => r.RecordsUpdated),
                TotalSkipped = g.Sum(r => r.RecordsSkipped),
                TotalFailed = g.Sum(r => r.RecordsFailed),
                CombinedErrors = null,
            })
            .FirstOrDefaultAsync(cancellationToken)
            ?? new JobRunBatchResultTotals();

        List<string> nonNullErrors = errors
            .Where(e => e != null)
            .Select(e => e!)
            .ToList();

        // Truncate combined errors to 2000 chars to match DB column constraint.
        string? combinedErrors = nonNullErrors.Count > 0
            ? TruncateString(string.Join("; ", nonNullErrors), 2000)
            : null;

        return new JobRunBatchResultTotals
        {
            TotalProcessed = totals.TotalProcessed,
            TotalCreated = totals.TotalCreated,
            TotalUpdated = totals.TotalUpdated,
            TotalSkipped = totals.TotalSkipped,
            TotalFailed = totals.TotalFailed,
            CombinedErrors = combinedErrors,
        };
    }

    /// <inheritdoc/>
    public Task<int> CountBatchResultsAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return _context.JobRunBatchResults.CountAsync(r => r.JobRunId == jobRunId, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }

    private static string TruncateString(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }
}
