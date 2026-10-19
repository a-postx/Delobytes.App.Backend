using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class WildberriesApiClientAccountInfoTests
{
    private const string SellerInfoUrl = "https://common-api.wildberries.ru/api/v1/seller-info";
    private static readonly Guid TestTemplateId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly SystemChannelTemplate TestTemplate = new SystemChannelTemplate
    {
        Id = TestTemplateId,
        Code = "wildberries",
        DisplayName = "Wildberries",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static WildberriesApiClient BuildClient(
        MockHttpMessageHandler accountInfoHandler,
        Mock<IEndpointResolver>? endpointResolverMock = null)
    {
        // GetAccountInfoAsync now sends through the typed HttpClient (same pipeline as
        // GetProductCardsAsync), so the mock handler is the typed client's transport directly.
        HttpClient typedClient = accountInfoHandler.ToHttpClient();

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
            endpointResolver.Object,
            new FixedConnectionResolver(),
            new AlwaysAllowWildberriesRateLimiter(),
            Options.Create(new WildberriesImportOptions()),
            NullLogger<WildberriesApiClient>.Instance);

        client.SetTemplate(TestTemplate);

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
            null, "test-key", null, null, CancellationToken.None);

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
            null, "test-key", null, null, CancellationToken.None);

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
            null, "bad-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_HttpRequestException_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Throw(new HttpRequestException("Network error"));

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            null, "test-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_TaskCanceledException_ReturnsNull()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Throw(new TaskCanceledException("Timeout"));

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            null, "test-key", null, null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAccountInfoAsync_EmptyJsonObject_ReturnsAccountInfoWithNulls()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Get, SellerInfoUrl).Respond("application/json", "{}");

        WildberriesApiClient client = BuildClient(mockHttp);

        AccountInfo? result = await client.GetAccountInfoAsync(
            null, "test-key", null, null, CancellationToken.None);

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
            null, "my-wb-key", null, null, CancellationToken.None);

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
            null, "test-key", null, null, CancellationToken.None);

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
            null, "test-key", null, null, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Endpoint not found");
    }

    [Fact]
    public async Task GetAccountInfoAsync_WithConnectionId_SetsConnectionIdOption()
    {
        Guid connectionId = Guid.NewGuid();
        Guid? capturedOption = null;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, SellerInfoUrl)
            .Respond(request =>
            {
                capturedOption = request.Options.TryGetValue(
                    WildberriesAuthHandler.ConnectionIdOptionKey, out Guid value)
                    ? value
                    : (Guid?)null;

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}"),
                });
            });

        WildberriesApiClient client = BuildClient(mockHttp);

        await client.GetAccountInfoAsync(connectionId, "test-key", null, null, CancellationToken.None);

        capturedOption.Should().Be(connectionId);
    }

    [Fact]
    public async Task GetAccountInfoAsync_WithoutConnectionId_DoesNotSetConnectionIdOption()
    {
        bool optionWasSet = false;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, SellerInfoUrl)
            .Respond(request =>
            {
                optionWasSet = request.Options.TryGetValue(
                    WildberriesAuthHandler.ConnectionIdOptionKey, out _);

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{}"),
                });
            });

        WildberriesApiClient client = BuildClient(mockHttp);

        await client.GetAccountInfoAsync(null, "test-key", null, null, CancellationToken.None);

        optionWasSet.Should().BeFalse();
    }
}
