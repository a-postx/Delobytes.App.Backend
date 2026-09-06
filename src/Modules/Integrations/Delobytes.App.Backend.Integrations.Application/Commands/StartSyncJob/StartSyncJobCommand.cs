using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartSyncJob;

/// <summary>
/// Command to start a synchronization job.
/// </summary>
public class StartSyncJobCommand : IRequest<StartSyncJobResponse>
{
    /// <summary>
    /// Gets or sets the connection identifier.
    /// </summary>
    public Guid ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the start of the date range for synchronization.
    /// </summary>
    public DateTimeOffset DateRangeFrom { get; set; }

    /// <summary>
    /// Gets or sets the end of the date range for synchronization.
    /// </summary>
    public DateTimeOffset DateRangeTo { get; set; }
}
