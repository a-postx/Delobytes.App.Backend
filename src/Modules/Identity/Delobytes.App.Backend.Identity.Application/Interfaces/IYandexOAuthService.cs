using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Application.Interfaces;

/// <summary>
/// Abstraction over the Yandex OAuth 2.0 and user-info HTTP calls.
/// </summary>
public interface IYandexOAuthService
{
    /// <summary>
    /// Exchanges an authorization code for a Yandex access token.
    /// </summary>
    /// <param name="code">The authorization code received from Yandex.</param>
    /// <param name="redirectUri">The redirect URI used in the original authorization request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Yandex access token string.</returns>
    public Task<string> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken);

    /// <summary>
    /// Fetches the authenticated user's profile from the Yandex ID user-info endpoint.
    /// </summary>
    /// <param name="accessToken">Yandex OAuth access token.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Yandex user profile.</returns>
    public Task<YandexUserInfo> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken);
}
