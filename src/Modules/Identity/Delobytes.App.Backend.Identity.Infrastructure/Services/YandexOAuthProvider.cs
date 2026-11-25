using System.Text.Json.Serialization;
using Delobytes.App.Backend.Identity.Application.Interfaces;
using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Yandex ID OAuth 2.0 provider.
/// Authorization endpoint: https://oauth.yandex.ru/authorize
/// Token endpoint:         https://oauth.yandex.ru/token
/// User-info endpoint:     https://login.yandex.ru/info
/// </summary>
internal sealed class YandexOAuthProvider : IOAuthProvider
{
    private const string TokenEndpoint = "https://oauth.yandex.ru/token";
    private const string UserInfoEndpoint = "https://login.yandex.ru/info?format=json";

    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;

    /// <summary>
    /// Initializes a new instance of the <see cref="YandexOAuthProvider"/> class.
    /// </summary>
    public YandexOAuthProvider(HttpClient httpClient, string clientId, string clientSecret)
    {
        _httpClient = httpClient;
        _clientId = clientId;
        _clientSecret = clientSecret;
    }

    /// <inheritdoc/>
    public OAuthProviderKind Kind => OAuthProviderKind.Yandex;

    /// <inheritdoc/>
    public string IdentityProviderName => "YandexID";

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
            "Yandex",
            code,
            redirectUri,
            cancellationToken);

        YandexUserInfoResponse userInfo = await OAuthHttp.GetUserInfoAsync<YandexUserInfoResponse>(
            _httpClient,
            UserInfoEndpoint,
            "OAuth",
            accessToken,
            "Yandex ID",
            cancellationToken);

        return new OAuthUserInfo
        {
            ExternalId = userInfo.Id,
            Email = userInfo.DefaultEmail ?? string.Empty,
        };
    }

    private sealed class YandexUserInfoResponse
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = default!;

        [JsonPropertyName("default_email")]
        public string? DefaultEmail { get; set; }
    }
}
