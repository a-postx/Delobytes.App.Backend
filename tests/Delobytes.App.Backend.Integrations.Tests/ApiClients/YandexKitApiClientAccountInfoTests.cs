using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class YandexKitApiClientAccountInfoTests
{
    private const string StoreUrl = "https://api.kit.yandex.net/v1/store";

    private static YandexKitApiClient BuildClient(MockHttpMessageHandler accountInfoHandler)
    {
        HttpClient typedClient = new HttpClient();

        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(accountInfoHandler.ToHttpClient());

        return new YandexKitApiClient(
            typedClient,
            factory.Object,
            NullLogger<YandexKitApiClient>.Instance);
    }

    [Fact]
    public async Task GetAccountInfoAsync_ValidResponse_MapsSlugToCustomerName()
    {
        string json = """{ "slug": "my-yandex-store" }""";

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, StoreUrl).Respond("application/json", json);

        YandexKitApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "yk-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("my-yandex-store");
        // Яндекс.Кит не предоставляет юрлицо и ИНН
        result.LegalName.Should().BeNull();
        result.Inn.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_SlugAbsentInResponse_CustomerNameIsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, StoreUrl).Respond("application/json", "{}");

        YandexKitApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "yk-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_NonSuccessStatusCode_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, StoreUrl).Respond(HttpStatusCode.Unauthorized);

        YandexKitApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "bad-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_HttpRequestException_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, StoreUrl).Throw(new HttpRequestException("timeout"));

        YandexKitApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "yk-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_TaskCanceledException_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, StoreUrl).Throw(new TaskCanceledException());

        YandexKitApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "yk-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_SendsAuthorizationHeaderWithBearer()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        mockHttp
            .When(HttpMethod.Get, StoreUrl)
            .WithHeaders("Authorization", "Bearer secret-yk-key")
            .Respond("application/json", """{ "slug": "shop-slug" }""");

        mockHttp.When(HttpMethod.Get, StoreUrl).Respond(HttpStatusCode.Unauthorized);

        YandexKitApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "secret-yk-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("shop-slug");
    }
}
