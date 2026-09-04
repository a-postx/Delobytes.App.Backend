namespace Delobytes.App.Backend.Identity.Application.Commands.AcceptInvitation;

/// <summary>
/// Response returned by AcceptInvitationCommand.
/// </summary>
public class AcceptInvitationResponse
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the assigned role.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the JWT access token with the new tenant context.
    /// </summary>
    public string AccessToken { get; set; } = default!;
}
