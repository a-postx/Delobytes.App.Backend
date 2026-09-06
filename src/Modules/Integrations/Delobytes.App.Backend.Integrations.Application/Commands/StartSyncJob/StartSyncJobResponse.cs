namespace Delobytes.App.Backend.Integrations.Application.Commands.StartSyncJob;

/// <summary>
/// Response for StartSyncJobCommand.
/// </summary>
public class StartSyncJobResponse
{
    /// <summary>
    /// Gets or sets the created sync job identifier.
    /// </summary>
    public Guid SyncJobId { get; set; }
}
