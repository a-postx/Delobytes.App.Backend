using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.SwitchTenant;

/// <summary>
/// Command to switch active tenant for a user.
/// </summary>
public class SwitchTenantCommand : IRequest<SwitchTenantResponse>
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the target tenant identifier to switch to.
    /// </summary>
    public Guid TargetTenantId { get; set; }
}
