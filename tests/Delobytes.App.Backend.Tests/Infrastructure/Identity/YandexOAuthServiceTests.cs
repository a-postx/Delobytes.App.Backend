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

public class YandexOAuthServiceTests
{
    private const string TokenEndpoint = "https://oauth.yandex.ru/token";
    private const string UserInfoEndpoint = "https://login.yandex.ru/info?format=json";
    private const string ClientId = "yandex-client-id";
    private const string ClientSecret = "yandex-client-secret";

    private readonly StubHttpMessageHandler _handler;
    private readonly YandexOAuthService _service;

    public YandexOAuthServiceTests()
    {
        _handler = new StubHttpMessageHandler();
        _service = new YandexOAuthService(new HttpClient(_handler), ClientId, ClientSecret);
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
        _handler.Enqueue(JsonResponse("{\"access_token\":\"yandex-access-token\",\"token_type\":\"bearer\",\"expires_in\":31536000}"));

        // Act
        string accessToken = await _service.ExchangeCodeForTokenAsync(
            "auth-code",
            "https://app.example.com/auth/yandex/callback",
            CancellationToken.None);

        // Assert
        accessToken.Should().Be("yandex-access-token");

        RecordedHttpRequest request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Post);
        request.RequestUri.Should().Be(new Uri(TokenEndpoint));

        Dictionary<string, string> form = StubHttpMessageHandler.ParseForm(request.Content!);
        form["grant_type"].Should().Be("authorization_code");
        form["code"].Should().Be("auth-code");
        form["client_id"].Should().Be(ClientId);
        form["client_secret"].Should().Be(ClientSecret);
        form["redirect_uri"].Should().Be("https://app.example.com/auth/yandex/callback");
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
            "https://app.example.com/auth/yandex/callback",
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
            "https://app.example.com/auth/yandex/callback",
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Yandex OAuth returned an empty token response.");
    }

    [Fact]
    public async Task GetUserInfoAsync_SendsOAuthSchemeAndMapsUserInfo()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"id\":\"yandex-id-001\",\"login\":\"ivan.yandex\",\"default_email\":\"ivan@yandex.ru\"}"));

        // Act
        YandexUserInfo userInfo = await _service.GetUserInfoAsync("yandex-access-token", CancellationToken.None);

        // Assert
        userInfo.Id.Should().Be("yandex-id-001");
        userInfo.Login.Should().Be("ivan.yandex");
        userInfo.DefaultEmail.Should().Be("ivan@yandex.ru");

        RecordedHttpRequest request = _handler.Requests.Should().ContainSingle().Subject;
        request.Method.Should().Be(HttpMethod.Get);
        request.RequestUri.Should().Be(new Uri(UserInfoEndpoint));
        request.Authorization.Should().Be("OAuth yandex-access-token");
    }

    [Fact]
    public async Task GetUserInfoAsync_WhenDefaultEmailMissing_ReturnsEmptyEmail()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"id\":\"yandex-id-002\",\"login\":\"no-email\"}"));

        // Act
        YandexUserInfo userInfo = await _service.GetUserInfoAsync("yandex-access-token", CancellationToken.None);

        // Assert
        userInfo.Id.Should().Be("yandex-id-002");
        userInfo.Login.Should().Be("no-email");
        userInfo.DefaultEmail.Should().BeEmpty();
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
        Func<Task> act = async () => await _service.GetUserInfoAsync("yandex-access-token", CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Yandex ID returned an empty user-info response.");
    }
}
