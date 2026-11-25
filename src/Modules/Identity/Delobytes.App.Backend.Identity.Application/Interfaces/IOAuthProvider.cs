using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Abstraction over one external identity provider's OAuth 2.0 authorization-code flow.
/// Implementations own everything provider-specific: endpoints, token transport,
/// payload shape and the local identity-provider name stored on the user.
/// </summary>
public interface IOAuthProvider
{
    /// <summary>
    /// Gets the provider kind used to select the implementation.
    /// </summary>
    public OAuthProviderKind Kind { get; }

    /// <summary>
    /// Gets the value stored in <c>User.IdentityProvider</c> for accounts created by this provider.
    /// </summary>
    public string IdentityProviderName { get; }

    /// <summary>
    /// Exchanges an authorization code for a provider token and fetches the user profile.
    /// </summary>
    /// <param name="code">Authorization code received from the provider.</param>
    /// <param name="redirectUri">Redirect URI used in the original authorization request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Normalized user profile.</returns>
    public Task<OAuthUserInfo> AuthorizeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken);
}
