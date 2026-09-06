using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
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
        => _context.SyncJobs.FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);

    /// <inheritdoc/>
    public Task<SyncJob?> FindByIdWithConnectionAsync(Guid id, CancellationToken cancellationToken)
        => _context.SyncJobs
            .Include(sj => sj.Connection)
            .ThenInclude(c => c.Channel)
            .FirstOrDefaultAsync(sj => sj.Id == id, cancellationToken);

    /// <inheritdoc/>
    public void Add(SyncJob syncJob)
        => _context.SyncJobs.Add(syncJob);

    /// <inheritdoc/>
    public void Update(SyncJob syncJob)
        => _context.SyncJobs.Update(syncJob);

    /// <inheritdoc/>
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken)
        => _context.SaveChangesAsync(cancellationToken);
}
