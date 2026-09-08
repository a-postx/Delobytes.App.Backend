using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class WildberriesAuthHandlerTests
{
    private static SystemChannelTemplate BuildTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "wildberries",
            DisplayName = "Wildberries",
            ApiBaseUrl = "https://suppliers-api.wildberries.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(string apiKey)
    {
        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            Name = "Wildberries",
            ApiKey = apiKey,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Channel = BuildTemplate(),
        };
    }

    private static HttpClient BuildHttpClientWithHandler(
        Mock<IConnectionResolver> resolverMock,
        MockHttpMessageHandler innerHandler)
    {
        WildberriesAuthHandler authHandler = new WildberriesAuthHandler(resolverMock.Object)
        {
            InnerHandler = innerHandler,
        };

        return new HttpClient(authHandler);
    }

    private static string? GetAuthorizationHeaderValue(HttpRequestMessage request)
    {
        if (request.Headers.TryGetValues("Authorization", out IEnumerable<string>? values))
        {
            return string.Join(",", values);
        }

        return null;
    }

    [Fact]
    public async Task SendAsync_AddsAuthorizationHeaderWithApiKey()
    {
        string apiKey = "wb-secret-key-12345";
        Connection connection = BuildConnection(apiKey);

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, "https://example.com/api/test")
            .WithHeaders("Authorization", apiKey)
            .Respond(HttpStatusCode.OK);

        // любой запрос без нужного заголовка → 401
        mockHttp.When(HttpMethod.Get, "https://example.com/api/test").Respond(HttpStatusCode.Unauthorized);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        HttpResponseMessage response = await client.GetAsync("https://example.com/api/test");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_HeaderValueIsApiKeyDirectly_NotBearerPrefixed()
    {
        // WildberriesAuthHandler передаёт ключ без Bearer — в отличие от Яндекс.Кит
        string apiKey = "plain-key-value";
        Connection connection = BuildConnection(apiKey);

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        string? capturedAuthHeader = null;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, "https://example.com/*")
            .Respond(request =>
            {
                capturedAuthHeader = GetAuthorizationHeaderValue(request);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
            });

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);
        await client.GetAsync("https://example.com/any");

        capturedAuthHeader.Should().Be(apiKey);
    }

    [Fact]
    public async Task SendAsync_ResolvesConnectionForWildberriesChannel()
    {
        Connection connection = BuildConnection("some-key");

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("wildberries", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);
        await client.GetAsync("https://example.com/anything");

        resolver.Verify(r => r.ResolveAsync("wildberries", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ResolverThrows_ExceptionPropagates()
    {
        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("wildberries", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No active connection for wildberries."));

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        Func<Task> act = () => client.GetAsync("https://example.com/anything");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*wildberries*");
    }
}
