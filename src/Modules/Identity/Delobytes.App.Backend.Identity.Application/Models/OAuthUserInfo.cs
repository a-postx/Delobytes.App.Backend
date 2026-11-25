namespace Delobytes.App.Backend.Identity.Application.Models;

/// <summary>
/// Provider-independent user profile obtained from an external identity provider.
/// </summary>
public class OAuthUserInfo
{
    /// <summary>
    /// Gets or sets the provider's unique user identifier.
    /// </summary>
    public string ExternalId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the user's email address as reported by the provider.
    /// </summary>
    public string Email { get; set; } = default!;
}
