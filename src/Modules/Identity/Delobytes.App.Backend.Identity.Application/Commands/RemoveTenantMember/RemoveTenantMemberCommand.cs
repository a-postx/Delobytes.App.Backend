using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.RemoveTenantMember;

/// <summary>
/// Command to remove a user from a tenant.
/// </summary>
public class RemoveTenantMemberCommand : IRequest<RemoveTenantMemberResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier to be removed.
    /// </summary>
    public Guid TargetUserId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is performing the removal.
    /// </summary>
    public Guid RemovedByUserId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
