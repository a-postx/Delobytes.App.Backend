using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class WildberriesApiClientGetProductCardsTests
{
    private const string ContentBaseUrl = "https://content-api.wildberries.ru";
    private const string GetCardsListUrl = "https://content-api.wildberries.ru/content/v2/get/cards/list";
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
        MockHttpMessageHandler mockHttp,
        Mock<IEndpointResolver>? endpointResolverMock = null)
    {
        HttpClient typedClient = mockHttp.ToHttpClient();

        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(mockHttp.ToHttpClient());

        Mock<IEndpointResolver> endpointResolver = endpointResolverMock ?? new Mock<IEndpointResolver>();

        if (endpointResolverMock == null)
        {
            endpointResolver
                .Setup(r => r.GetEndpointUrlAsync(
                    TestTemplateId,
                    ChannelEndpointType.Content,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(ContentBaseUrl);
        }

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            factory.Object,
            endpointResolver.Object,
            NullLogger<WildberriesApiClient>.Instance);

        client.SetTemplate(TestTemplate);

        return client;
    }

    [Fact]
    public async Task GetProductCardsAsync_ValidResponse_ReturnsCardsWithCursor()
    {
        string json = """
        {
            "cards": [
                {
                    "nmID": 123456789,
                    "vendorCode": "VENDOR-001",
                    "title": "Тестовый товар",
                    "description": "Описание товара",
                    "sizes": [
                        {
                            "chrtID": 111222333,
                            "techSize": "M",
                            "skus": ["1234567890123", "9876543210987"]
                        }
                    ]
                }
            ],
            "cursor": {
                "updatedAt": "2024-01-15T12:30:00Z",
                "nmID": 123456789,
                "total": 150
            }
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, GetCardsListUrl)
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.StatusCode.Should().Be(200);
        result.Data.Should().NotBeNull();
        result.Data!.Cards.Should().HaveCount(1);
        result.Data.TotalCount.Should().Be(150);
        result.Data.NextCursor.Should().NotBeNull();
        result.Data.NextCursor!.UpdatedAt.Should().Be("2024-01-15T12:30:00Z");
        result.Data.NextCursor.ProductId.Should().Be(123456789);

        WildberriesCardSnapshot card = result.Data.Cards.First();
        card.NmId.Should().Be(123456789);
        card.Name.Should().Be("Тестовый товар");
        card.VendorCode.Should().Be("VENDOR-001");
        card.Description.Should().Be("Описание товара");
        card.Barcodes.Should().HaveCount(2);
        card.Barcodes.Should().Contain("1234567890123");
        card.Barcodes.Should().Contain("9876543210987");
    }

    [Fact]
    public async Task GetProductCardsAsync_WithCursor_SendsCursorInRequest()
    {
        string json = """
        {
            "cards": [],
            "cursor": null
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, GetCardsListUrl)
            .With(req =>
            {
                string body = req.Content!.ReadAsStringAsync().Result;
                return body.Contains("\"updatedAt\":\"2024-01-10T10:00:00Z\"") &&
                       body.Contains("\"nmID\":987654321");
            })
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ProductCardsCursor cursor = new ProductCardsCursor
        {
            UpdatedAt = "2024-01-10T10:00:00Z",
            ProductId = 987654321
        };

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(cursor, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Cards.Should().BeEmpty();
        result.Data.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetProductCardsAsync_LastPage_ReturnsNullCursor()
    {
        string json = """
        {
            "cards": [
                {
                    "nmID": 111,
                    "vendorCode": "LAST",
                    "title": "Last item"
                }
            ],
            "cursor": null
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetProductCardsAsync_EmptyResponse_ReturnsEmptyCards()
    {
        string json = """
        {
            "cards": [],
            "cursor": null
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Cards.Should().BeEmpty();
        result.Data.TotalCount.Should().Be(0);
        result.Data.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetProductCardsAsync_UnauthorizedResponse_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(HttpStatusCode.Unauthorized);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        result.ErrorMessage.Should().Contain("Authentication failed");
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_ForbiddenResponse_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(HttpStatusCode.Forbidden);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        result.ErrorMessage.Should().Contain("Authentication failed");
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_RateLimitExceeded_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.Add("Retry-After", "30");

        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(_ => response);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(429);
        result.ErrorMessage.Should().Contain("Rate limit exceeded");
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_ServerError_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(HttpStatusCode.InternalServerError);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);
        result.ErrorMessage.Should().Contain("API error");
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_HttpRequestException_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Throw(new HttpRequestException("Network error"));

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Network error");
        result.StatusCode.Should().BeNull();
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_TaskCanceledException_ReturnsTimeout()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Throw(new TaskCanceledException("Timeout"));

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Request timeout");
        result.StatusCode.Should().BeNull();
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_InvalidJson_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", "{ invalid json");

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid JSON");
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_NullResponseBody_ReturnsFailure()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", "null");

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Empty response");
        result.Data!.Cards.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_CardWithoutSizes_MapsBarcodes()
    {
        string json = """
        {
            "cards": [
                {
                    "nmID": 999,
                    "vendorCode": "NO-SIZES",
                    "title": "Product without sizes"
                }
            ],
            "cursor": null
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Cards.Should().HaveCount(1);
        WildberriesCardSnapshot card = result.Data.Cards.First();
        card.Barcodes.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProductCardsAsync_InvalidLimit_ThrowsException()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        WildberriesApiClient client = BuildClient(mockHttp);

        Func<Task> act = async () => await client.GetProductCardsAsync(null, 0, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Limit must be between 1 and 100*");
    }

    [Fact]
    public async Task GetProductCardsAsync_LimitTooHigh_ThrowsException()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        WildberriesApiClient client = BuildClient(mockHttp);

        Func<Task> act = async () => await client.GetProductCardsAsync(null, 101, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>()
            .WithMessage("*Limit must be between 1 and 100*");
    }

    [Fact]
    public async Task GetProductCardsAsync_UsesContentEndpoint()
    {
        string customUrl = "https://custom-content-api.wildberries.ru";
        string json = """{ "cards": [], "cursor": null }""";

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Content,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(customUrl);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, $"{customUrl}/content/v2/get/cards/list")
            .Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp, endpointResolver);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        endpointResolver.Verify(
            r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Content,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetProductCardsAsync_MapsChannelSpecificData()
    {
        string json = """
        {
            "cards": [
                {
                    "nmID": 555,
                    "vendorCode": "TEST",
                    "title": "Test",
                    "brand": "TestBrand",
                    "dimensions": {
                        "length": 10,
                        "width": 20,
                        "height": 30
                    }
                }
            ],
            "cursor": null
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        WildberriesCardSnapshot card = result.Data!.Cards.First();
        card.ChannelSpecificData.Should().NotBeNullOrEmpty();
        card.ChannelSpecificData.Should().Contain("TestBrand");
        card.ChannelSpecificData.Should().Contain("dimensions");
    }
}
