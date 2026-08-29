using Delobytes.App.Backend.Identity.Domain.Enums;

namespace Delobytes.App.Backend.Identity.Domain.Entities;

/// <summary>
/// Represents an invitation to join a tenant.
/// </summary>
public class Invitation
{
    /// <summary>
    /// Gets or sets the invitation unique identifier.
    /// </summary>
    public Guid Id { get; set; }

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
    /// Gets or sets the invitation token (unique, used for acceptance link).
    /// </summary>
    public string Token { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date and time when the invitation was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the invitation expires.
    /// </summary>
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the invitation has been accepted.
    /// </summary>
    public bool IsAccepted { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the invitation was accepted.
    /// </summary>
    public DateTimeOffset? AcceptedAt { get; set; }

    /// <summary>
    /// Gets or sets the user identifier who accepted the invitation.
    /// </summary>
    public Guid? AcceptedByUserId { get; set; }

    /// <summary>
    /// Navigation property: the tenant.
    /// </summary>
    public Tenant Tenant { get; set; } = default!;
}
