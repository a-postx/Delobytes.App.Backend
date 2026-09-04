namespace Delobytes.App.Backend.Identity.Application.Commands.CreateInvitation;

/// <summary>
/// Response returned by CreateInvitationCommand.
/// </summary>
public class CreateInvitationResponse
{
    /// <summary>
    /// Gets or sets the invitation unique identifier.
    /// </summary>
    public Guid InvitationId { get; set; }

    /// <summary>
    /// Gets or sets the invitation token.
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the email address of the invitee.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the role that will be assigned.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the invitation expiration date and time.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
