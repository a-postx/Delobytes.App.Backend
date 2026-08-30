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
}
