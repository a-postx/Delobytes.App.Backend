using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Implements the Google OAuth 2.0 token exchange and user-info retrieval.
/// Authorization endpoint: https://accounts.google.com/o/oauth2/v2/auth
/// Token endpoint:         https://oauth2.googleapis.com/token
/// User-info endpoint:     https://www.googleapis.com/oauth2/v3/userinfo
/// </summary>
internal sealed class GoogleOAuthService : IGoogleOAuthService
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;

    /// <summary>
    /// Initializes a new instance of the <see cref="GoogleOAuthService"/> class.
    /// </summary>
    public GoogleOAuthService(HttpClient httpClient, string clientId, string clientSecret)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <inheritdoc/>
    public async Task<string> ExchangeCodeForTokenAsync(
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = _clientId,
            ["client_secret"] = _clientSecret,
            ["redirect_uri"] = redirectUri,
        };

        using FormUrlEncodedContent content = new FormUrlEncodedContent(formData);

        HttpResponseMessage response = await _httpClient.PostAsync(
            TokenEndpoint, content, cancellationToken);

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        GoogleTokenResponse tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Google OAuth returned an empty token response.");

        return tokenResponse.AccessToken;
    }

    /// <inheritdoc/>
    public async Task<GoogleUserInfo> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        GoogleUserInfoResponse infoResponse = JsonSerializer.Deserialize<GoogleUserInfoResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Google returned an empty user-info response.");

        return new GoogleUserInfo
        {
            Sub = infoResponse.Sub,
            Email = infoResponse.Email ?? string.Empty,
            EmailVerified = infoResponse.EmailVerified,
        };
    }

    // ── Internal DTO models ───────────────────────────────────────────────────

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = default!;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = default!;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }

    private sealed class GoogleUserInfoResponse
    {
        [JsonPropertyName("sub")]
        public string Sub { get; set; } = default!;

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("email_verified")]
        public bool EmailVerified { get; set; }
    }
}
