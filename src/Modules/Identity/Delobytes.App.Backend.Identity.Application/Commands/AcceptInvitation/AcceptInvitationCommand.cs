using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.AcceptInvitation;

/// <summary>
/// Command to accept an invitation to join a tenant.
/// </summary>
public class AcceptInvitationCommand : IRequest<AcceptInvitationResponse>
{
    /// <summary>
    /// Gets or sets the invitation token.
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user identifier who is accepting the invitation.
    /// </summary>
    public Guid UserId { get; set; }
}
