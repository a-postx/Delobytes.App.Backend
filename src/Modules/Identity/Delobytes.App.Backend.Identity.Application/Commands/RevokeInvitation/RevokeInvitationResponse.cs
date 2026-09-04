namespace Delobytes.App.Backend.Identity.Application.Commands.RevokeInvitation;

/// <summary>
/// Response returned by RevokeInvitationCommand.
/// </summary>
public class RevokeInvitationResponse
{
    /// <summary>
    /// Gets or sets a value indicating whether the revocation was successful.
    /// </summary>
    public bool Success { get; set; }
}
