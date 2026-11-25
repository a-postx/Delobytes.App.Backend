using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using Delobytes.App.Backend.Identity.Application.Models;

namespace Delobytes.App.Backend.Identity.Infrastructure.Services;

/// <summary>
/// Shared OAuth 2.0 HTTP steps: authorization-code exchange and bearer-style user-info retrieval.
/// Both providers use the same grant shape; only endpoints and the user-info scheme differ.
/// </summary>
internal static class OAuthHttp
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <summary>
    /// Posts an authorization-code grant and returns the provider access token.
    /// </summary>
    public static async Task<string> ExchangeCodeAsync(
        HttpClient httpClient,
        string tokenEndpoint,
        string clientId,
        string clientSecret,
        string providerName,
        string code,
        string redirectUri,
        CancellationToken cancellationToken)
    {
        Dictionary<string, string> formData = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["redirect_uri"] = redirectUri,
        };

        using FormUrlEncodedContent content = new FormUrlEncodedContent(formData);

        HttpResponseMessage response = await httpClient.PostAsync(tokenEndpoint, content, cancellationToken);

        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        OAuthTokenResponse tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{providerName} OAuth returned an empty token response.");

        return tokenResponse.AccessToken;
    }

    /// <summary>
    /// Calls a user-info endpoint with the given authorization scheme and returns the raw payload.
    /// </summary>
    public static async Task<TResponse> GetUserInfoAsync<TResponse>(
        HttpClient httpClient,
        string userInfoEndpoint,
        string authorizationScheme,
        string accessToken,
        string providerName,
        CancellationToken cancellationToken)
        where TResponse : class
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, userInfoEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue(authorizationScheme, accessToken);

        HttpResponseMessage response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        string json = await response.Content.ReadAsStringAsync(cancellationToken);

        return JsonSerializer.Deserialize<TResponse>(json, JsonOptions)
            ?? throw new InvalidOperationException($"{providerName} returned an empty user-info response.");
    }

    private sealed class OAuthTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = default!;
    }
}
