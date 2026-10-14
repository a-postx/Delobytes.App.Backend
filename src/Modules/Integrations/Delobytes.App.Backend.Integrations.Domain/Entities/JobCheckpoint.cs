namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Per-task internal state of a single job run (e.g. a pagination cursor), persisted so the
/// task can resume after a cancellation or restart. Dies together with its owning JobRun;
/// unlike <see cref="SyncCheckpoint"/> it carries no business meaning once the run is gone.
/// </summary>
public class JobCheckpoint
{
    /// <summary>
    /// Gets or sets the identifier, shared with and referencing the owning <see cref="JobRun"/>.
    /// </summary>
    public Guid JobRunId { get; set; }

    /// <summary>
    /// Gets or sets the JSON-serialized internal state (e.g. pagination cursor).
    /// </summary>
    public string Payload { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date and time when this checkpoint was last written.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: the owning job run.
    /// </summary>
    public JobRun JobRun { get; set; } = default!;
}
