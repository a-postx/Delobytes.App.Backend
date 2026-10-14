using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for JobCheckpoint (per-task internal state, e.g. a pagination cursor).
/// </summary>
public interface IJobCheckpointRepository
{
    /// <summary>
    /// Finds the checkpoint for the given job run.
    /// </summary>
    Task<JobCheckpoint?> GetByJobRunIdAsync(Guid jobRunId, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the checkpoint if none exists for its JobRunId, otherwise updates the existing one.
    /// </summary>
    Task UpsertAsync(JobCheckpoint checkpoint, CancellationToken cancellationToken);
}
