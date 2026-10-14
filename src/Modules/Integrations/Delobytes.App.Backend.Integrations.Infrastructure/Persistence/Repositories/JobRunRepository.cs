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
    public Task<List<JobRun>> GetRunningJobsOlderThanAsync(DateTimeOffset threshold, CancellationToken cancellationToken)
    {
        // Watchdog must see every tenant's stuck jobs, not just the caller's current tenant.
        return _context.JobRuns
            .IgnoreQueryFilters()
            .Where(j => j.Status == JobStatus.Running && j.StartedAt != null && j.StartedAt < threshold)
            .ToListAsync(cancellationToken);
    }
}
