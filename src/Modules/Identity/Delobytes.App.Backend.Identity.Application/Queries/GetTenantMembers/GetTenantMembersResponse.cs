namespace Delobytes.App.Backend.Identity.Application.Queries.GetTenantMembers;

/// <summary>
/// Response returned by GetTenantMembersQuery.
/// </summary>
public class GetTenantMembersResponse
{
    /// <summary>
    /// Gets or sets the list of active members.
    /// </summary>
    public List<TenantMemberInfo> Members { get; set; } = new List<TenantMemberInfo>();

    /// <summary>
    /// Gets or sets the list of pending invitations.
    /// </summary>
    public List<PendingInvitationInfo> PendingInvitations { get; set; } = new List<PendingInvitationInfo>();
}

/// <summary>
/// Information about a tenant member.
/// </summary>
public class TenantMemberInfo
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the membership identifier.
    /// </summary>
    public Guid MembershipId { get; set; }

    /// <summary>
    /// Gets or sets the user email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user role within the tenant.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date when the membership was created.
    /// </summary>
    public DateTimeOffset JoinedAt { get; set; }
}

/// <summary>
/// Information about a pending invitation.
/// </summary>
public class PendingInvitationInfo
{
    /// <summary>
    /// Gets or sets the invitation identifier.
    /// </summary>
    public Guid InvitationId { get; set; }

    /// <summary>
    /// Gets or sets the invitee email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the role that will be assigned.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets the invitation token.
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date when the invitation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date when the invitation expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }
}
