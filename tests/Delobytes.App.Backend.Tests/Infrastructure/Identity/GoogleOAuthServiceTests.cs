using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

public class GoogleOAuthServiceTests
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";
    private const string ClientId = "google-client-id";
    private const string ClientSecret = "google-client-secret";

    private readonly StubHttpMessageHandler _handler;
    private readonly GoogleOAuthService _service;

    public GoogleOAuthServiceTests()
    {
        _handler = new StubHttpMessageHandler();
        _service = new GoogleOAuthService(new HttpClient(_handler), ClientId, ClientSecret);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_PostsAuthorizationCodeGrantAndReturnsAccessToken()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"google-access-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));

        // Act
        string accessToken = await _service.ExchangeCodeForTokenAsync(
            "auth-code",
            "https://app.example.com/auth/google/callback",
            CancellationToken.None);

        // Assert
        accessToken.Should().Be("google-access-token");

        RecordedHttpRequest request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri(TokenEndpoint));

        Dictionary<string, string> form = StubHttpMessageHandler.ParseForm(request.Content!);
        form["grant_type"].Should().Be("authorization_code");
        form["code"].Should().Be("auth-code");
        form["client_id"].Should().Be(ClientId);
        form["client_secret"].Should().Be(ClientSecret);
        form["redirect_uri"].Should().Be("https://app.example.com/auth/google/callback");
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenProviderReturnsErrorStatus_ThrowsHttpRequestException()
    {
        // Arrange
        _handler.Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json"),
        });

        // Act
        Func<Task> act = async () => await _service.ExchangeCodeForTokenAsync(
            "bad-code",
            "https://app.example.com/auth/google/callback",
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task ExchangeCodeForTokenAsync_WhenResponseBodyIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("null"));

        // Act
        Func<Task> act = async () => await _service.ExchangeCodeForTokenAsync(
            "auth-code",
            "https://app.example.com/auth/google/callback",
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Google OAuth returned an empty token response.");
    }

    [Fact]
    public async Task GetUserInfoAsync_SendsBearerTokenAndMapsUserInfo()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"sub\":\"google-sub-001\",\"email\":\"ivan@gmail.com\",\"email_verified\":true}"));

        // Act
        GoogleUserInfo userInfo = await _service.GetUserInfoAsync("google-access-token", CancellationToken.None);

        // Assert
        userInfo.Sub.Should().Be("google-sub-001");
        userInfo.Email.Should().Be("ivan@gmail.com");
        userInfo.EmailVerified.Should().BeTrue();

        RecordedHttpRequest request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri(UserInfoEndpoint));
        request.Authorization.Should().Be("Bearer google-access-token");
    }

    [Fact]
    public async Task GetUserInfoAsync_WhenEmailMissing_ReturnsEmptyEmail()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"sub\":\"google-sub-002\",\"email_verified\":false}"));

        // Act
        GoogleUserInfo userInfo = await _service.GetUserInfoAsync("google-access-token", CancellationToken.None);

        // Assert
        userInfo.Sub.Should().Be("google-sub-002");
        userInfo.Email.Should().BeEmpty();
        userInfo.EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task GetUserInfoAsync_WhenProviderReturnsErrorStatus_ThrowsHttpRequestException()
    {
        // Arrange
        _handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // Act
        Func<Task> act = async () => await _service.GetUserInfoAsync("expired-token", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetUserInfoAsync_WhenResponseBodyIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("null"));

        // Act
        Func<Task> act = async () => await _service.GetUserInfoAsync("google-access-token", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Google returned an empty user-info response.");
    }
}
