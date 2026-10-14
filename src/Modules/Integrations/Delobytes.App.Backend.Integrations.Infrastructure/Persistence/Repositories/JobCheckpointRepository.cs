using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of IJobCheckpointRepository.
/// </summary>
public class JobCheckpointRepository : IJobCheckpointRepository
{
    private readonly IntegrationsDbContext _context;

    /// <summary>
    /// Initializes a new instance of the <see cref="JobCheckpointRepository"/> class.
    /// </summary>
    public JobCheckpointRepository(IntegrationsDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public Task<JobCheckpoint?> GetByJobRunIdAsync(Guid jobRunId, CancellationToken cancellationToken)
    {
        return _context.JobCheckpoints.FirstOrDefaultAsync(c => c.JobRunId == jobRunId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken)
    {
        JobCheckpoint? existing = await _context.JobCheckpoints
            .FirstOrDefaultAsync(c => c.JobRunId == checkpoint.JobRunId, cancellationToken);

        if (existing is null)
        {
            _context.JobCheckpoints.Add(checkpoint);
        }
        else
        {
            existing.Payload = checkpoint.Payload;
            existing.UpdatedAt = checkpoint.UpdatedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
