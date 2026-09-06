namespace Delobytes.App.Backend.Integrations.Application.Events;

/// <summary>
/// Event published when a sync job needs to be processed.
/// </summary>
public record StartSyncJobEvent
{
    /// <summary>
    /// Gets or sets the sync job identifier.
    /// </summary>
    public Guid SyncJobId { get; init; }

    /// <summary>
    /// Gets or sets the connection identifier.
    /// </summary>
    public Guid ConnectionId { get; init; }
}
