using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Api;

/// <summary>
/// End-to-end authentication over the real HTTP pipeline: registration, password login,
/// tenant creation, the current-user projection, and OAuth callbacks with stubbed providers.
/// </summary>
public sealed class AuthApiTests : IClassFixture<AuthApiHost>
{
    private const string Password = "Secret123!";

    private readonly AuthApiHost _host;

    public AuthApiTests(AuthApiHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task Post_Register_ReturnsSetupSessionForNewLocalAccount()
    {
        using HttpResponseMessage response = await PostJsonAsync(
            "/api/auth/register",
            new { email = UniqueEmail("register"), password = Password, displayName = "Ivan" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("userId").GetGuid().Should().NotBeEmpty();
        body.GetProperty("requiresTenantSetup").GetBoolean().Should().BeTrue();
        body.GetProperty("success").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Post_Register_SecondTimeWithSameEmail_Returns409WithIdentityCode()
    {
        string email = UniqueEmail("duplicate");

        using HttpResponseMessage first = await PostJsonAsync(
            "/api/auth/register",
            new { email, password = Password });
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        using HttpResponseMessage second = await PostJsonAsync(
            "/api/auth/register",
            new { email, password = Password });

        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        JsonElement body = await ReadJsonAsync(second);
        body.GetProperty("code").GetString().Should().Be("identity.user.email_conflict");
    }

    [Fact]
    public async Task Post_Login_UnknownEmail_Returns401()
    {
        using HttpResponseMessage response = await PostJsonAsync(
            "/api/auth/login",
            new { email = UniqueEmail("missing"), password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().Should().Be("common.unauthorized");
    }

    [Fact]
    public async Task Post_Login_WrongPassword_Returns401()
    {
        string email = UniqueEmail("wrong-password");
        await RegisterAsync(email);

        using HttpResponseMessage response = await PostJsonAsync(
            "/api/auth/login",
            new { email, password = "not-the-password" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Post_Login_ValidCredentials_ReturnsSession()
    {
        string email = UniqueEmail("login");
        await RegisterAsync(email);

        using HttpResponseMessage response = await PostJsonAsync(
            "/api/auth/login",
            new { email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("requiresTenantSetup").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Get_Me_ForLocalAccount_ReportsLocalProviderAndPasswordCapability()
    {
        string email = UniqueEmail("local-me");
        string setupToken = await RegisterAsync(email);
        string tenantToken = await CreateTenantAsync(setupToken, "Local account tenant");

        JsonElement me = await GetMeAsync(tenantToken);

        me.GetProperty("email").GetString().Should().Be(email);
        me.GetProperty("tenantName").GetString().Should().Be("Local account tenant");
        me.GetProperty("identityProvider").GetString().Should().Be("Local");
        me.GetProperty("canChangePassword").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Get_Me_ForOAuthAccount_ReportsProviderAndNoPasswordCapability()
    {
        string setupToken = await OAuthCallbackAsync(
            "/api/auth/yandex/callback", "https://app.example.com/auth/yandex/callback");
        string tenantToken = await CreateTenantAsync(setupToken, "Yandex account tenant");

        JsonElement me = await GetMeAsync(tenantToken);

        me.GetProperty("identityProvider").GetString().Should().Be("YandexID");
        me.GetProperty("canChangePassword").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Post_GoogleCallback_ReturnsSetupSession()
    {
        string setupToken = await OAuthCallbackAsync(
            "/api/auth/google/callback", "https://app.example.com/auth/google/callback");

        setupToken.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Post_ChangePassword_ForOAuthAccount_Returns422()
    {
        string setupToken = await OAuthCallbackAsync(
            "/api/auth/yandex/callback", "https://app.example.com/auth/yandex/callback");

        using HttpResponseMessage response = await PostJsonAsync(
            "/api/auth/change-password",
            new { currentPassword = "whatever", newPassword = "AnotherPass123!" },
            setupToken);

        // The UI hides the section, but the server must refuse it independently.
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        JsonElement body = await ReadJsonAsync(response);
        body.GetProperty("code").GetString().Should().Be("common.validation_failed");
    }

    private static string UniqueEmail(string prefix)
    {
        return $"{prefix}-{Guid.NewGuid():N}@example.com";
    }

    private async Task<string> RegisterAsync(string email)
    {
        using HttpResponseMessage response = await PostJsonAsync(
            "/api/auth/register",
            new { email, password = Password });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task<string> OAuthCallbackAsync(string path, string redirectUri)
    {
        using HttpResponseMessage response = await PostJsonAsync(
            path,
            new { code = "stub-code", redirectUri });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        return body.GetProperty("accessToken").GetString()!;
    }

    private async Task<string> CreateTenantAsync(string accessToken, string tenantName)
    {
        using HttpResponseMessage response = await PostJsonAsync(
            "/api/auth/create-tenant",
            new { tenantName },
            accessToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        JsonElement body = await ReadJsonAsync(response);
        string token = body.GetProperty("accessToken").GetString()!;
        token.Should().NotBeNullOrWhiteSpace();
        return token;
    }

    private async Task<JsonElement> GetMeAsync(string accessToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await _host.Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await ReadJsonAsync(response);
    }

    private Task<HttpResponseMessage> PostJsonAsync(
        string path,
        object body,
        string? accessToken = null)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };

        if (accessToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        }

        return _host.Client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
