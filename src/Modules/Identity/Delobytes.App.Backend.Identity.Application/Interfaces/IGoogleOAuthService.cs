using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Abstraction over the Google OAuth 2.0 and user-info HTTP calls.
/// </summary>
public interface IGoogleOAuthService
{
    /// <summary>
    /// Exchanges an authorization code for a Google access token.
    /// </summary>
    /// <param name="code">The authorization code received from Google.</param>
    /// <param name="redirectUri">The redirect URI used in the original authorization request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Google access token string.</returns>
    public Task<string> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the authenticated user's profile from the Google user-info endpoint.
    /// </summary>
    /// <param name="accessToken">Google OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Google user profile.</returns>
    public Task<GoogleUserInfo> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken);
}
