namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Type-specific data for a JobRun of type OrdersSync. Keyed by the owning JobRun's Id.
/// Currently has no type-specific fields of its own: the sync watermark is tracked in
/// <see cref="SyncCheckpoint"/>, which survives this run's deletion.
/// </summary>
public class OrdersImportJob
{
    /// <summary>
    /// Gets or sets the identifier, shared with and referencing the owning <see cref="JobRun"/>.
    /// </summary>
    public Guid JobRunId { get; set; }

    /// <summary>
    /// Navigation property: the owning job run.
    /// </summary>
    public JobRun JobRun { get; set; } = default!;
}
