using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.Register;

/// <summary>
/// Command to register a new user with email/password.
/// </summary>
public class RegisterCommand : IRequest<RegisterResponse>
{
    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the password.
    /// </summary>
    public string Password { get; set; } = default!;

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }
}
