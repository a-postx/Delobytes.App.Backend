namespace Delobytes.App.Backend.Identity.Domain.Entities;

/// <summary>
/// Represents a user in the system.
/// User is not tied to a specific tenant (no fixed TenantId).
/// User can belong to multiple tenants via TenantMembership.
/// </summary>
public class User
{
    /// <summary>
    /// Gets or sets the user unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the external identity provider identifier (Yandex ID or local email).
    /// </summary>
    public string ExternalId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the identity provider type (e.g., "YandexID", "Local").
    /// </summary>
    public string IdentityProvider { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the canonical email used for lookups and uniqueness.
    /// </summary>
    public string NormalizedEmail { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the password hash (only for local authentication).
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the user was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the user last logged in.
    /// </summary>
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user account is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the instant before which issued tokens are no longer accepted.
    /// Set when the password changes so existing sessions stop working.
    /// </summary>
    public DateTimeOffset? TokensValidAfter { get; set; }

    /// <summary>
    /// Gets or sets the last active tenant identifier for this user.
    /// Used to restore the user's tenant context when they log in from any device.
    /// </summary>
    public Guid? LastActiveTenantId { get; set; }

    /// <summary>
    /// Navigation property: tenant memberships.
    /// </summary>
    public ICollection<TenantMembership> Memberships { get; set; } = new List<TenantMembership>();
}
