using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Per (ConnectionId, JobType) business watermark, surviving deletion of the JobRun that last
/// updated it. Used to resume delta synchronization (e.g. orders "lastChangeDate" cursor)
/// across pod restarts and job run history cleanup.
/// </summary>
public class SyncCheckpoint : ITenantScoped
{
    /// <summary>
    /// Gets or sets the unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the connection identifier.
    /// </summary>
    public Guid ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the job type this checkpoint tracks.
    /// </summary>
    public JobType JobType { get; set; }

    /// <summary>
    /// Gets or sets the timestamp of the last change observed from the source system
    /// (e.g. marketplace "lastChangeDate"). Null before the first successful run.
    /// </summary>
    public DateTimeOffset? LastChangedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time of the last successful run completion.
    /// </summary>
    public DateTimeOffset LastSuccessAt { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the JobRun that last updated this checkpoint.
    /// Null if that run's history has since been removed.
    /// </summary>
    public Guid? LastJobRunId { get; set; }

    /// <summary>
    /// Navigation property: the connection this checkpoint belongs to.
    /// </summary>
    public Connection Connection { get; set; } = default!;

    /// <summary>
    /// Navigation property: the job run that last updated this checkpoint.
    /// </summary>
    public JobRun? LastJobRun { get; set; }
}
