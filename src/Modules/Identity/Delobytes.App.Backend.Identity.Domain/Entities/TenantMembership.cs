using Delobytes.App.Backend.Identity.Domain.Enums;

namespace Delobytes.App.Backend.Identity.Domain.Entities;

/// <summary>
/// Represents the many-to-many relationship between User and Tenant with an associated Role.
/// </summary>
public class TenantMembership
{
    /// <summary>
    /// Gets or sets the membership unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the user role within this tenant.
    /// </summary>
    public Role Role { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the membership was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the membership is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Navigation property: the user.
    /// </summary>
    public User User { get; set; } = default!;

    /// <summary>
    /// Navigation property: the tenant.
    /// </summary>
    public Tenant Tenant { get; set; } = default!;
}
