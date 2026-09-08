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

public class WildberriesApiClientAccountInfoTests
{
    private const string SellerInfoUrl = "https://common-api.wildberries.ru/api/v1/seller-info";

    private static WildberriesApiClient BuildClient(MockHttpMessageHandler accountInfoHandler)
    {
        // typed HttpClient идёт через WildberriesAuthHandler в продакшне;
        // в тестах GetAccountInfoAsync использует IHttpClientFactory, поэтому typed — заглушка
        HttpClient typedClient = new HttpClient();

        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(accountInfoHandler.ToHttpClient());

        return new WildberriesApiClient(
            typedClient,
            factory.Object,
            NullLogger<WildberriesApiClient>.Instance);
    }

    [Fact]
    public async Task GetAccountInfoAsync_ValidResponse_MapsAllFields()
    {
        string json = """
            {
                "name": "ООО Ромашка",
                "tin": "7701234567",
                "trademark": "Ромашка Маркет"
            }
            """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, SellerInfoUrl)
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("Ромашка Маркет");
        result.LegalName.Should().Be("ООО Ромашка");
        result.Inn.Should().Be("7701234567");
    }

    [Fact]
    public async Task GetAccountInfoAsync_PartialResponse_MapsAvailableFields()
    {
        // trademark отсутствует — CustomerName должен быть null
        string json = """{ "name": "ООО Тест", "tin": "1234567890" }""";

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().BeNull();
        result.LegalName.Should().Be("ООО Тест");
        result.Inn.Should().Be("1234567890");
    }

    [Fact]
    public async Task GetAccountInfoAsync_NonSuccessStatusCode_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Respond(HttpStatusCode.Unauthorized);

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "bad-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_HttpRequestException_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Throw(new HttpRequestException("Network error"));

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_TaskCanceledException_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Throw(new TaskCanceledException("Timeout"));

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_EmptyJsonObject_ReturnsAccountInfoWithNulls()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Respond("application/json", "{}");

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().BeNull();
        result.LegalName.Should().BeNull();
        result.Inn.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_SendsAuthorizationHeaderWithBearer()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        mockHttp
            .When(HttpMethod.Get, SellerInfoUrl)
            .WithHeaders("Authorization", "Bearer my-wb-key")
            .Respond("application/json", """{ "trademark": "Магазин" }""");

        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Respond(HttpStatusCode.Unauthorized);

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "my-wb-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("Магазин");
    }
}
