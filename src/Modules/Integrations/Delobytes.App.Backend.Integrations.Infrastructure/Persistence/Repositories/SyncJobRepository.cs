using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ISyncJobRepository.
/// </summary>
public class SyncJobRepository : ISyncJobRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncJobRepository"/> class.
    /// </summary>
    /// <param name="context">Database context.</param>
    public SyncJobRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<SyncJob?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.SyncJobs.FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<SyncJob?> FindByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken)
    {
        return _context.SyncJobs
                .Include(sj => sj.Connection)
                .ThenInclude(c => c.SystemChannelTemplate)
                .FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);
    }

    /// <inheritdoc/>
    public void Add(SyncJob syncJob)
    {
        _context.SyncJobs.Add(syncJob);
    }

    /// <inheritdoc/>
    public void Update(SyncJob syncJob)
    {
        _context.SyncJobs.Update(syncJob);
    }

    /// <inheritdoc/>
    public Task<List<SyncJob>> GetProductsImportJobsAsync(CancellationToken cancellationToken)
    {
        return _context.SyncJobs
            .Where(j => j.JobType == JobType.ProductsImport)
            .OrderByDescending(j => j.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public Task<bool> HasActivePendingOrRunningJobForConnectionAsync(Guid connectionId, CancellationToken cancellationToken)
    {
        return _context.SyncJobs.AnyAsync(
            j => j.ConnectionId == connectionId
                && j.JobType == JobType.ProductsImport
                && (j.Status == SyncJobStatus.Pending || j.Status == SyncJobStatus.Running),
            cancellationToken);
    }

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
    {
        return _context.SaveChangesAsync(cancellationToken);
    }
}
