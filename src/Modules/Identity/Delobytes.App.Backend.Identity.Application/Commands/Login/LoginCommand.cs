using MediatR;

namespace Delobytes.App.Backend.Identity.Application.Commands.Login;

/// <summary>
/// Command to authenticate a user via email/password or external provider.
/// </summary>
public class LoginCommand : IRequest<LoginResponse>
{
    /// <summary>
    /// Gets or sets the email address.
    /// </summary>
    public string Email { get; set; } = default!;

    /// <summary>
    /// Gets or sets the password (for local authentication).
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Gets or sets the external identity provider (e.g., "YandexID").
    /// </summary>
    public string? IdentityProvider { get; set; }

    /// <summary>
    /// Gets or sets the external identifier from the identity provider.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// Gets or sets the display name.
    /// </summary>
    public string? DisplayName { get; set; }
}
