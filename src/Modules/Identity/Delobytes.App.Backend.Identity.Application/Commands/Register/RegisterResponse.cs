namespace Delobytes.App.Backend.Identity.Application.Commands.Register;

/// <summary>
/// Response from register command.
/// </summary>
public class RegisterResponse
{
    /// <summary>
    /// Gets or sets the created user identifier.
    /// </summary>
    public Guid UserId { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether registration was successful.
    /// </summary>
    public bool Success { get; set; }
}
