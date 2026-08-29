namespace Delobytes.App.Backend.Identity.Application.Commands.Register;

/// <summary>
/// Response from register command.
/// </summary>
public class RegisterResponse
{
    /// <summary>
    /// Gets or sets the JWT access token.
    /// </summary>
    public string AccessToken { get; set; } = default!;

    /// <summary>
    /// Gets or sets the created user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether registration was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the user needs to set up a tenant.
    /// </summary>
    public bool RequiresTenantSetup { get; set; }
}
