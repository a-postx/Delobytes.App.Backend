namespace Delobytes.App.Backend.Identity.Application.Queries.GetCurrentUser;

/// <summary>
/// Response returned by GetCurrentUserQuery.
/// </summary>
public class GetCurrentUserResponse
{
    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the user display name.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Gets or sets the user email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the active tenant identifier.
    /// </summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Gets or sets the active tenant name.
    /// </summary>
    public string TenantName { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user role in the active tenant.
    /// </summary>
    public string Role { get; set; } = default!;

    /// <summary>
    /// Gets or sets all tenants the user belongs to.
    /// </summary>
    public List<UserTenantInfo> Tenants { get; set; } = new List<UserTenantInfo>();
}

/// <summary>
/// Information about a tenant the user belongs to.
/// </summary>
public class UserTenantInfo
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
    /// Gets or sets the user role within this tenant.
    /// </summary>
    public string Role { get; set; } = default!;
}
