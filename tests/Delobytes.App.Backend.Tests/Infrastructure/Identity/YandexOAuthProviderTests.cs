using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Identity.Application.Models;
using Delobytes.App.Backend.Identity.Infrastructure.Services;
using Delobytes.App.Backend.Tests.TestSupport;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Infrastructure.Identity;

public class YandexOAuthProviderTests
{
    private const string TokenEndpoint = "https://oauth.yandex.ru/token";
    private const string UserInfoEndpoint = "https://login.yandex.ru/info?format=json";
    private const string ClientId = "yandex-client-id";
    private const string ClientSecret = "yandex-client-secret";
    private const string RedirectUri = "https://app.example.com/auth/yandex/callback";

    private readonly StubHttpMessageHandler _handler;
    private readonly YandexOAuthProvider _provider;

    public YandexOAuthProviderTests()
    {
        _handler = new StubHttpMessageHandler();
        _provider = new YandexOAuthProvider(new HttpClient(_handler), ClientId, ClientSecret);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public void ProviderMetadata_IdentifiesYandexAccounts()
    {
        _provider.Kind.Should().Be(OAuthProviderKind.Yandex);
        _provider.IdentityProviderName.Should().Be("YandexID");
    }

    [Fact]
    public async Task AuthorizeAsync_PostsAuthorizationCodeGrantAndReturnsNormalizedUserInfo()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"yandex-access-token\",\"token_type\":\"bearer\"}"));
        _handler.Enqueue(JsonResponse("{\"id\":\"yandex-id-001\",\"login\":\"ivan.yandex\",\"default_email\":\"ivan@yandex.ru\"}"));

        // Act
        OAuthUserInfo userInfo = await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        userInfo.ExternalId.Should().Be("yandex-id-001");
        userInfo.Email.Should().Be("ivan@yandex.ru");

        _handler.Requests.Should().HaveCount(2);

        RecordedHttpRequest tokenRequest = _handler.Requests[0];
        tokenRequest.Method.Should().Be(HttpMethod.Post);
        tokenRequest.RequestUri.Should().Be(new Uri(TokenEndpoint));

        Dictionary<string, string> form = StubHttpMessageHandler.ParseForm(tokenRequest.Content!);
        form["grant_type"].Should().Be("authorization_code");
        form["code"].Should().Be("auth-code");
        form["client_id"].Should().Be(ClientId);
        form["client_secret"].Should().Be(ClientSecret);
        form["redirect_uri"].Should().Be(RedirectUri);

        RecordedHttpRequest userInfoRequest = _handler.Requests[1];
        userInfoRequest.Method.Should().Be(HttpMethod.Get);
        userInfoRequest.RequestUri.Should().Be(new Uri(UserInfoEndpoint));
        userInfoRequest.Authorization.Should().Be("OAuth yandex-access-token");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenDefaultEmailMissing_ReturnsEmptyEmail()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"yandex-access-token\"}"));
        _handler.Enqueue(JsonResponse("{\"id\":\"yandex-id-002\",\"login\":\"no-email\"}"));

        // Act
        OAuthUserInfo userInfo = await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        userInfo.ExternalId.Should().Be("yandex-id-002");
        userInfo.Email.Should().BeEmpty();
    }

    [Fact]
    public async Task AuthorizeAsync_WhenTokenEndpointReturnsErrorStatus_ThrowsHttpRequestException()
    {
        // Arrange
        _handler.Enqueue(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json"),
        });

        // Act
        Func<Task> act = async () => await _provider.AuthorizeAsync("bad-code", RedirectUri, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AuthorizeAsync_WhenTokenResponseBodyIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("null"));

        // Act
        Func<Task> act = async () => await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Yandex OAuth returned an empty token response.");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenUserInfoReturnsErrorStatus_ThrowsHttpRequestException()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"yandex-access-token\"}"));
        _handler.Enqueue(new HttpResponseMessage(HttpStatusCode.Unauthorized));

        // Act
        Func<Task> act = async () => await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task AuthorizeAsync_WhenUserInfoResponseBodyIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"yandex-access-token\"}"));
        _handler.Enqueue(JsonResponse("null"));

        // Act
        Func<Task> act = async () => await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Yandex ID returned an empty user-info response.");
    }
}
