using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;
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
    private static readonly Guid TestTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static WildberriesApiClient BuildClient(
        MockHttpMessageHandler accountInfoHandler,
        Mock<IEndpointResolver>? endpointResolverMock = null)
    {
        // typed HttpClient идёт через WildberriesAuthHandler в продакшне;
        // в тестах GetAccountInfoAsync использует IHttpClientFactory, поэтому typed — заглушка
        HttpClient typedClient = new HttpClient();

        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(accountInfoHandler.ToHttpClient());

        Mock<IEndpointResolver> endpointResolver = endpointResolverMock ?? new Mock<IEndpointResolver>();

        if (endpointResolverMock == null)
        {
            endpointResolver
                .Setup(r => r.GetEndpointUrlAsync(
                    TestTemplateId,
                    ChannelEndpointType.Common,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://common-api.wildberries.ru");
        }

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            factory.Object,
            endpointResolver.Object,
            NullLogger<WildberriesApiClient>.Instance);

        client.SetTemplateId(TestTemplateId);

        return client;
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

    [Fact]
    public async Task GetAccountInfoAsync_UsesEndpointResolverForCommonEndpoint()
    {
        string customUrl = "https://custom-common-api.wildberries.ru";
        string json = """{ "name": "ООО Тест", "trademark": "Тест" }""";

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Common,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(customUrl);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{customUrl}/api/v1/seller-info")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp, endpointResolver);

        AccountInfo? result = await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.LegalName.Should().Be("ООО Тест");

        endpointResolver.Verify(
            r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Common,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetAccountInfoAsync_ThrowsWhenTemplateIdNotSet()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        HttpClient typedClient = new HttpClient();
        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(mockHttp.ToHttpClient());

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            factory.Object,
            endpointResolver.Object,
            NullLogger<WildberriesApiClient>.Instance);

        // SetTemplateId НЕ вызван

        Func<Task> act = async () => await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Template ID must be set*");
    }

    [Fact]
    public async Task GetAccountInfoAsync_ReturnsNullWhenEndpointResolverThrows()
    {
        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Common,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Endpoint not found"));

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        WildberriesApiClient client = BuildClient(mockHttp, endpointResolver);

        Func<Task> act = async () => await client.GetAccountInfoAsync(
            "test-key", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Endpoint not found");
    }

}
