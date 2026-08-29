namespace Delobytes.App.Backend.Identity.Application.Commands.Login;

/// <summary>
/// Response from login command.
/// </summary>
public class LoginResponse
{
    /// <summary>
    /// Gets or sets the JWT access token.
    /// </summary>
    public string AccessToken { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    public Guid? TenantId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user needs to set up a tenant.
    /// </summary>
    public bool RequiresTenantSetup { get; set; }
}
