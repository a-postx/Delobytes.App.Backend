using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of ISyncCheckpointRepository.
/// </summary>
public class SyncCheckpointRepository : ISyncCheckpointRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncCheckpointRepository"/> class.
    /// </summary>
    public SyncCheckpointRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<SyncCheckpoint?> GetAsync(Guid connectionId, JobType jobType, CancellationToken cancellationToken)
    {
        return _context.SyncCheckpoints
            .FirstOrDefaultAsync(c => c.ConnectionId == connectionId && c.JobType == jobType, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(SyncCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        SyncCheckpoint? existing = await _context.SyncCheckpoints
            .FirstOrDefaultAsync(
                c => c.ConnectionId == checkpoint.ConnectionId && c.JobType == checkpoint.JobType,
                cancellationToken);

        if (existing is null)
        {
            if (checkpoint.Id == Guid.Empty)
            {
                checkpoint.Id = Guid.NewGuid();
            }

            _context.SyncCheckpoints.Add(checkpoint);
        }
        else
        {
            existing.LastChangedAt = checkpoint.LastChangedAt;
            existing.LastSuccessAt = checkpoint.LastSuccessAt;
            existing.LastJobRunId = checkpoint.LastJobRunId;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
