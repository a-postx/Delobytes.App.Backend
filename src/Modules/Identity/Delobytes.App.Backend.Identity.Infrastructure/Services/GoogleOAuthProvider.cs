using System.Text.Json.Serialization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Google OAuth 2.0 provider.
/// Authorization endpoint: https://accounts.google.com/o/oauth2/v2/auth
/// Token endpoint:         https://oauth2.googleapis.com/token
/// User-info endpoint:     https://www.googleapis.com/oauth2/v3/userinfo
/// </summary>
internal sealed class GoogleOAuthProvider : IOAuthProvider
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleOAuthProvider"/> class.
    /// </summary>
    public GoogleOAuthProvider(HttpClient httpClient, string clientId, string clientSecret)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <inheritdoc/>
    public OAuthProviderKind Kind => OAuthProviderKind.Google;

    /// <inheritdoc/>
    public string IdentityProviderName => "GoogleID";

    /// <inheritdoc/>
    public async Task<OAuthUserInfo> AuthorizeAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        string accessToken = await OAuthHttp.ExchangeCodeAsync(
            _httpClient,
            TokenEndpoint,
            _clientId,
            _clientSecret,
            "Google",
            code,
            redirectUri,
            cancellationToken);

        GoogleUserInfoResponse userInfo = await OAuthHttp.GetUserInfoAsync<GoogleUserInfoResponse>(
            _httpClient,
            UserInfoEndpoint,
            "Bearer",
            accessToken,
            "Google",
            cancellationToken);

        return new OAuthUserInfo
        {
            ExternalId = userInfo.Sub,
            Email = userInfo.Email ?? string.Empty,
        };
    }

    private sealed class GoogleUserInfoResponse
    {
        [JsonPropertyName("sub")]
        public string Sub { get; set; } = default!;

        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}
