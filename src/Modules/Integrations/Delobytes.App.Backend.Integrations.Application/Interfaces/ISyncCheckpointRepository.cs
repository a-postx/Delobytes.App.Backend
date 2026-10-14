using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Repository interface for SyncCheckpoint (per connection/job-type business watermark).
/// </summary>
public interface ISyncCheckpointRepository
{
    /// <summary>
    /// Finds the checkpoint for the given connection and job type.
    /// </summary>
    Task<SyncCheckpoint?> GetAsync(Guid connectionId, JobType jobType, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the checkpoint if none exists for its (ConnectionId, JobType), otherwise updates
    /// the existing one.
    /// </summary>
    Task UpsertAsync(SyncCheckpoint checkpoint, CancellationToken cancellationToken);
}
