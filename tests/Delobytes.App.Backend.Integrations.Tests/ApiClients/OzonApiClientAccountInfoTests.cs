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

public class OzonApiClientAccountInfoTests
{
    private const string SellerInfoUrl = "https://api-seller.ozon.ru/v1/seller/info";

    private static OzonApiClient BuildClient(MockHttpMessageHandler accountInfoHandler)
    {
        HttpClient typedClient = new HttpClient();

        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(accountInfoHandler.ToHttpClient());

        return new OzonApiClient(
            typedClient,
            factory.Object,
            NullLogger<OzonApiClient>.Instance);
    }

    private static Dictionary<string, string> ValidSettings()
    {
        return new Dictionary<string, string> { ["sellerId"] = "12345" };
    }

    [Fact]
    public async Task GetAccountInfoAsync_ValidResponse_MapsAllFields()
    {
        string json = """
            {
                "company": {
                    "name": "Ozon Store",
                    "legal_name": "ООО Озон Трейд",
                    "inn": "9999888877"
                }
            }
            """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, SellerInfoUrl).Respond("application/json", json);

        OzonApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "ozon-key", null, ValidSettings(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("Ozon Store");
        result.LegalName.Should().Be("ООО Озон Трейд");
        result.Inn.Should().Be("9999888877");
    }

    [Fact]
    public async Task GetAccountInfoAsync_PartialResponse_MapsAvailableFields()
    {
        string json = """{ "company": { "name": "My Shop", "legal_name": "ИП Иванов" } }""";

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, SellerInfoUrl).Respond("application/json", json);

        OzonApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "ozon-key", null, ValidSettings(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("My Shop");
        result.LegalName.Should().Be("ИП Иванов");
        result.Inn.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_NullSettings_ReturnsNull()
    {
        OzonApiClient client = BuildClient(new MockHttpMessageHandler());

        AccountInfo? result = await client.GetAccountInfoAsync(
            "ozon-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_MissingSellerIdInSettings_ReturnsNull()
    {
        Dictionary<string, string> settings = new Dictionary<string, string>
        {
            ["other"] = "value",
        };

        OzonApiClient client = BuildClient(new MockHttpMessageHandler());

        AccountInfo? result = await client.GetAccountInfoAsync(
            "ozon-key", null, settings, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_NonSuccessStatusCode_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, SellerInfoUrl).Respond(HttpStatusCode.Forbidden);

        OzonApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "bad-key", null, ValidSettings(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_HttpRequestException_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, SellerInfoUrl).Throw(new HttpRequestException("fail"));

        OzonApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "ozon-key", null, ValidSettings(), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_SendsCorrectHeaders()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        mockHttp
            .When(HttpMethod.Post, SellerInfoUrl)
            .WithHeaders("Client-Id", "12345")
            .WithHeaders("Api-Key", "my-ozon-key")
            .Respond("application/json", """{ "company": { "name": "Shop" } }""");

        mockHttp.When(HttpMethod.Post, SellerInfoUrl).Respond(HttpStatusCode.Forbidden);

        OzonApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "my-ozon-key", null, ValidSettings(), CancellationToken.None);

        result.Should().NotBeNull();
        result!.CustomerName.Should().Be("Shop");
    }
}
