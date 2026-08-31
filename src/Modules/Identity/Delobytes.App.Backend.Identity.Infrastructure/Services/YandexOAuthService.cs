using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Implements the Yandex OAuth 2.0 token exchange and user-info retrieval.
/// Authorization endpoint: https://oauth.yandex.ru/authorize
/// Token endpoint:         https://oauth.yandex.ru/token
/// User-info endpoint:     https://login.yandex.ru/info
/// </summary>
internal sealed class YandexOAuthService : IYandexOAuthService
{
    private const string TokenEndpoint = "https://oauth.yandex.ru/token";
    private const string UserInfoEndpoint = "https://login.yandex.ru/info?format=json";

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;

    /// <summary>
    /// Initializes a new instance of the <see cref="YandexOAuthService"/> class.
    /// </summary>
    public YandexOAuthService(HttpClient httpClient, string clientId, string clientSecret)
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

        YandexTokenResponse tokenResponse = JsonSerializer.Deserialize<YandexTokenResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Yandex OAuth returned an empty token response.");

        return tokenResponse.AccessToken;
    }

    /// <inheritdoc/>
    public async Task<YandexUserInfo> GetUserInfoAsync(
        string accessToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("OAuth", accessToken);

        HttpResponseMessage response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        YandexUserInfoResponse infoResponse = JsonSerializer.Deserialize<YandexUserInfoResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException("Yandex ID returned an empty user-info response.");

        return new YandexUserInfo
        {
            Id = infoResponse.Id,
            Login = infoResponse.Login,
            DefaultEmail = infoResponse.DefaultEmail ?? string.Empty,
        };
    }

    // ── Internal DTO models ───────────────────────────────────────────────────

    private sealed class YandexTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = default!;

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = default!;

        [JsonPropertyName("expires_in")]
        public long ExpiresIn { get; set; }
    }

    private sealed class YandexUserInfoResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = default!;

        [JsonPropertyName("login")]
        public string Login { get; set; } = default!;

        [JsonPropertyName("default_email")]
        public string? DefaultEmail { get; set; }
    }
}
