using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateMembershipRole;

/// <summary>
/// Command to update a user's role within a tenant.
/// </summary>
public class UpdateMembershipRoleCommand : IRequest<UpdateMembershipRoleResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier whose role is being updated.
    /// </summary>
    public Guid TargetUserId { get; set; }

    /// <summary>
    /// Gets or sets the new role.
    /// </summary>
    public Role NewRole { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is performing the update.
    /// </summary>
    public Guid UpdatedByUserId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
