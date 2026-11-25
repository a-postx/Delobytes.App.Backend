using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;

namespace Delobytes.App.Backend.Tests.Api;

/// <summary>
/// Verifies that the anonymous account-creating endpoints are throttled per client address
/// with the production defaults (register: 5 per minute, OAuth callbacks: 10 per minute).
/// </summary>
public sealed class AuthRateLimitTests : IClassFixture<RateLimitAuthApiHost>
{
    private readonly RateLimitAuthApiHost _host;

    public AuthRateLimitTests(RateLimitAuthApiHost host)
    {
        _host = host;
    }

    [Fact]
    public async Task Post_Register_AbovePerMinuteLimit_Returns429()
    {
        // Register is limited to 5 requests per minute; the sixth must be rejected.
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            using HttpResponseMessage allowed = await PostJsonAsync(
                "/api/auth/register",
                new
                {
                    email = $"limit-{attempt}-{Guid.NewGuid():N}@example.com",
                    password = "Secret123!",
                });

            allowed.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "attempt {0} is within the register limit",
                attempt);
        }

        using HttpResponseMessage rejected = await PostJsonAsync(
            "/api/auth/register",
            new { email = $"limit-6-{Guid.NewGuid():N}@example.com", password = "Secret123!" });

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Post_OAuthCallback_AbovePerMinuteLimit_Returns429()
    {
        // Both callback routes share one policy limited to 10 requests per minute.
        for (int attempt = 1; attempt <= 10; attempt++)
        {
            string path = attempt % 2 == 0
                ? "/api/auth/google/callback"
                : "/api/auth/yandex/callback";

            using HttpResponseMessage allowed = await PostJsonAsync(
                path,
                new { code = "stub-code", redirectUri = "https://app.example.com/auth/callback" });

            allowed.StatusCode.Should().Be(
                HttpStatusCode.OK,
                "attempt {0} is within the callback limit",
                attempt);
        }

        using HttpResponseMessage rejected = await PostJsonAsync(
            "/api/auth/yandex/callback",
            new { code = "stub-code", redirectUri = "https://app.example.com/auth/callback" });

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private Task<HttpResponseMessage> PostJsonAsync(string path, object body)
    {
        HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Content = JsonContent.Create(body),
        };

        return _host.Client.SendAsync(request);
    }
}
