using Delobytes.App.Backend.Identity.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.CreateInvitation;

/// <summary>
/// Command to create an invitation to join a tenant.
/// </summary>
public class CreateInvitationCommand : IRequest<CreateInvitationResponse>
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the email address of the invitee.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the role that will be assigned upon acceptance.
    /// </summary>
    public Role Role { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who is creating the invitation.
    /// </summary>
    public Guid InvitedByUserId { get; set; }

    /// <inheritdoc/>
    public Role[] AllowedRoles => new[] { Role.Administrator };
}
