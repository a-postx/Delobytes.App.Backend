namespace Delobytes.App.Backend.Identity.Domain.Entities;

/// <summary>
/// Represents a tenant (organization/workspace) in the multi-tenant system.
/// </summary>
public class Tenant
{
    /// <summary>
    /// Gets or sets the tenant unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the tenant name (required).
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the date and time when the tenant was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the tenant was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Navigation property: tenant memberships.
    /// </summary>
    public ICollection<TenantMembership> Memberships { get; set; } = new List<TenantMembership>();
}
