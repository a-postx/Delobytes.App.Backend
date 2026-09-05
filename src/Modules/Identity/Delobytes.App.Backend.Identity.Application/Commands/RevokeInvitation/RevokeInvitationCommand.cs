using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.RevokeInvitation;

/// <summary>
/// Command to revoke (delete) an invitation.
/// </summary>
public class RevokeInvitationCommand : IRequest<RevokeInvitationResponse>
{
    /// <summary>
    /// Gets or sets the invitation identifier.
    /// </summary>
    public Guid InvitationId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is revoking the invitation.
    /// </summary>
    public Guid RevokedByUserId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
