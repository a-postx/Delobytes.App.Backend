namespace Delobytes.App.Backend.Identity.Application.Commands.UpdateMembershipRole;

/// <summary>
/// Response returned by UpdateMembershipRoleCommand.
/// </summary>
public class UpdateMembershipRoleResponse
{
    /// <summary>
    /// Gets or sets the membership identifier.
    /// </summary>
    public Guid MembershipId { get; set; }

    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the new role.
    /// </summary>
    public string Role { get; set; } = default!;
}
