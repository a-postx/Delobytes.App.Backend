using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Integrations.Application.DTOs.Jobs;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.StartJob;

/// <summary>
/// Starts a job run of the requested type for a connection through the unified
/// POST /jobs/{type} endpoint.
/// </summary>
public class StartJobCommand : IRequest<StartJobResponse>, IRequireRole
{
    /// <summary>
    /// Gets or sets the resolved job type.
    /// </summary>
    public JobType JobType { get; set; }

    public Guid ConnectionId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
