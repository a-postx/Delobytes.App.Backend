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

public class YandexKitAuthHandlerTests
{
    private static SystemChannelTemplate BuildTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "yandex.kit",
            DisplayName = "Яндекс.Кит",
            ApiBaseUrl = "https://api.kit.yandex.net",
            ApiVersion = "v1",
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
            Name = "Яндекс.Кит",
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
        YandexKitAuthHandler authHandler = new YandexKitAuthHandler(resolverMock.Object)
        {
            InnerHandler = innerHandler,
        };

        return new HttpClient(authHandler);
    }

    [Fact]
    public async Task SendAsync_AddsAuthorizationHeaderWithBearerPrefix()
    {
        string apiKey = "yk-token-xyz";
        Connection connection = BuildConnection(apiKey);

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("yandex.kit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, "https://example.com/api")
            .WithHeaders("Authorization", $"Bearer {apiKey}")
            .Respond(HttpStatusCode.OK);

        mockHttp.When(HttpMethod.Get, "https://example.com/api").Respond(HttpStatusCode.Unauthorized);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        HttpResponseMessage response = await client.GetAsync("https://example.com/api");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_HeaderValueHasBearerPrefix_NotPlainKey()
    {
        // Яндекс.Кит требует Bearer, в отличие от Wildberries
        string apiKey = "raw-key";
        Connection connection = BuildConnection(apiKey);

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("yandex.kit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        string? capturedAuthHeader = null;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, "https://example.com/*")
            .Respond(_ =>
            {
                capturedAuthHeader = _.Headers.TryGetValues("Authorization",
                    out System.Collections.Generic.IEnumerable<string>? vals)
                    ? string.Join(",", vals)
                    : null;
                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);
        await client.GetAsync("https://example.com/any");

        capturedAuthHeader.Should().Be($"Bearer {apiKey}");
    }

    [Fact]
    public async Task SendAsync_ResolvesConnectionForYandexKitChannel()
    {
        Connection connection = BuildConnection("some-key");

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("yandex.kit", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);
        await client.GetAsync("https://example.com/anything");

        resolver.Verify(r => r.ResolveAsync("yandex.kit", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ResolverThrows_ExceptionPropagates()
    {
        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("yandex.kit", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No active connection for yandex.kit."));

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        Func<Task> act = () => client.GetAsync("https://example.com/anything");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*yandex.kit*");
    }
}
