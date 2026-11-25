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

public class GoogleOAuthProviderTests
{
    private const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    private const string UserInfoEndpoint = "https://www.googleapis.com/oauth2/v3/userinfo";
    private const string ClientId = "google-client-id";
    private const string ClientSecret = "google-client-secret";
    private const string RedirectUri = "https://app.example.com/auth/google/callback";

    private readonly StubHttpMessageHandler _handler;
    private readonly GoogleOAuthProvider _provider;

    public GoogleOAuthProviderTests()
    {
        _handler = new StubHttpMessageHandler();
        _provider = new GoogleOAuthProvider(new HttpClient(_handler), ClientId, ClientSecret);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
    }

    [Fact]
    public void ProviderMetadata_IdentifiesGoogleAccounts()
    {
        _provider.Kind.Should().Be(OAuthProviderKind.Google);
        _provider.IdentityProviderName.Should().Be("GoogleID");
    }

    [Fact]
    public async Task AuthorizeAsync_PostsAuthorizationCodeGrantAndReturnsNormalizedUserInfo()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"google-access-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}"));
        _handler.Enqueue(JsonResponse("{\"sub\":\"google-sub-001\",\"email\":\"ivan@gmail.com\",\"email_verified\":true}"));

        // Act
        OAuthUserInfo userInfo = await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        userInfo.ExternalId.Should().Be("google-sub-001");
        userInfo.Email.Should().Be("ivan@gmail.com");

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
        userInfoRequest.Authorization.Should().Be("Bearer google-access-token");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenEmailMissing_ReturnsEmptyEmail()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"google-access-token\"}"));
        _handler.Enqueue(JsonResponse("{\"sub\":\"google-sub-002\"}"));

        // Act
        OAuthUserInfo userInfo = await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        userInfo.ExternalId.Should().Be("google-sub-002");
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
            .WithMessage("Google OAuth returned an empty token response.");
    }

    [Fact]
    public async Task AuthorizeAsync_WhenUserInfoReturnsErrorStatus_ThrowsHttpRequestException()
    {
        // Arrange
        _handler.Enqueue(JsonResponse("{\"access_token\":\"google-access-token\"}"));
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
        _handler.Enqueue(JsonResponse("{\"access_token\":\"google-access-token\"}"));
        _handler.Enqueue(JsonResponse("null"));

        // Act
        Func<Task> act = async () => await _provider.AuthorizeAsync("auth-code", RedirectUri, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Google returned an empty user-info response.");
    }
}
