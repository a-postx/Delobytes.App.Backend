using System.Net;
using System.Net.Http;
using System.Text.Json;
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

public class OzonAuthHandlerTests
{
    private static SystemChannelTemplate BuildTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "ozon",
            DisplayName = "Ozon",
            ApiBaseUrl = "https://api-seller.ozon.ru",
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(string apiKey, string sellerId)
    {
        string settings = JsonSerializer.Serialize(new { sellerId });

        return new Connection
        {
            Id = Guid.NewGuid(),
            ChannelId = Guid.NewGuid(),
            Name = "Ozon",
            ApiKey = apiKey,
            Settings = settings,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
            Channel = BuildTemplate(),
        };
    }

    private static HttpClient BuildHttpClientWithHandler(
        Mock<IConnectionResolver> resolverMock,
        MockHttpMessageHandler innerHandler)
    {
        OzonAuthHandler authHandler = new OzonAuthHandler(resolverMock.Object)
        {
            InnerHandler = innerHandler,
        };

        return new HttpClient(authHandler);
    }

    [Fact]
    public async Task SendAsync_AddsClientIdAndApiKeyHeaders()
    {
        string apiKey = "ozon-api-key-abc";
        string sellerId = "99887766";
        Connection connection = BuildConnection(apiKey, sellerId);

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("ozon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, "https://example.com/api")
            .WithHeaders("Client-Id", sellerId)
            .WithHeaders("Api-Key", apiKey)
            .Respond(HttpStatusCode.OK);

        mockHttp.When(HttpMethod.Post, "https://example.com/api").Respond(HttpStatusCode.Forbidden);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        HttpResponseMessage response = await client.PostAsync("https://example.com/api",
            new StringContent("{}"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SendAsync_ResolvesConnectionForOzonChannel()
    {
        Connection connection = BuildConnection("key", "12345");

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("ozon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);
        await client.GetAsync("https://example.com/anything");

        resolver.Verify(r => r.ResolveAsync("ozon", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ConnectionWithNoSettings_ThrowsInvalidOperationException()
    {
        Connection connection = BuildConnection("key", "123");
        connection.Settings = null;

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("ozon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        Func<Task> act = () => client.GetAsync("https://example.com/anything");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sellerId*");
    }

    [Fact]
    public async Task SendAsync_SettingsWithoutSellerIdKey_ThrowsInvalidOperationException()
    {
        Connection connection = BuildConnection("key", "123");
        connection.Settings = JsonSerializer.Serialize(new { otherField = "value" });

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("ozon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        Func<Task> act = () => client.GetAsync("https://example.com/anything");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sellerId*");
    }

    [Fact]
    public async Task SendAsync_EmptySellerIdInSettings_ThrowsInvalidOperationException()
    {
        Connection connection = BuildConnection("key", "123");
        connection.Settings = JsonSerializer.Serialize(new { sellerId = "" });

        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("ozon", It.IsAny<CancellationToken>()))
            .ReturnsAsync(connection);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        Func<Task> act = () => client.GetAsync("https://example.com/anything");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*sellerId*");
    }

    [Fact]
    public async Task SendAsync_ResolverThrows_ExceptionPropagates()
    {
        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();
        resolver
            .Setup(r => r.ResolveAsync("ozon", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("No active connection for ozon."));

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When("*").Respond(HttpStatusCode.OK);

        HttpClient client = BuildHttpClientWithHandler(resolver, mockHttp);

        Func<Task> act = () => client.GetAsync("https://example.com/anything");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*ozon*");
    }
}
