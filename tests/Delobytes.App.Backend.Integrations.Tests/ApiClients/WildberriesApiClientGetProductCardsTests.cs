using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

public class WildberriesApiClientGetProductCardsTests
{
    private const string ContentBaseUrl = "https://content-api.wildberries.ru";
    private const string GetCardsListUrl = "https://content-api.wildberries.ru/content/v2/get/cards/list";

    /// <summary>
    /// Negligible but non-zero retry delay for the tests that force a deserialization retry.
    /// Zero is unusable here: <see cref="Task.Delay(TimeSpan, CancellationToken)"/> with a zero
    /// delay completes before yielding.
    /// </summary>
    private static readonly TimeSpan NoRetryDelay = TimeSpan.FromMilliseconds(1);

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
        Mock<IEndpointResolver>? endpointResolverMock = null,
        ILogger<WildberriesApiClient>? logger = null,
        TimeSpan? retryDelay = null)
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
            logger ?? NullLogger<WildberriesApiClient>.Instance);

        client.DeserializationRetryDelay = retryDelay ?? NoRetryDelay;
        client.SetTemplate(TestTemplate);

        return client;
    }

    private static List<string> BuildLogCapture(Mock<ILogger<WildberriesApiClient>> loggerMock)
    {
        List<string> loggedMessages = new List<string>();

        loggerMock
            .Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback(new InvocationAction(invocation =>
            {
                object state = invocation.Arguments[2];
                Exception? exception = (Exception?)invocation.Arguments[3];
                Delegate formatter = (Delegate)invocation.Arguments[4];

                string message = formatter.DynamicInvoke(state, exception)?.ToString() ?? string.Empty;
                loggedMessages.Add(message);
            }));

        return loggedMessages;
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
                    "dimensions": {
                        "length": 30,
                        "width": 20,
                        "height": 10,
                        "weightBrutto": 0.5
                    },
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

        card.LengthCm.Should().Be(30);
        card.WidthCm.Should().Be(20);
        card.HeightCm.Should().Be(10);
        card.WeightKg.Should().Be(0.5m);
    }

    [Fact]
    public async Task GetProductCardsAsync_CardWithoutDimensions_ReturnsNullDimensions()
    {
        string json = """
        {
            "cards": [
                {
                    "nmID": 111,
                    "vendorCode": "NO-DIMENSIONS",
                    "title": "Товар без габаритов"
                }
            ],
            "cursor": null
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        WildberriesCardSnapshot card = result.Data!.Cards.First();
        card.LengthCm.Should().BeNull();
        card.WidthCm.Should().BeNull();
        card.HeightCm.Should().BeNull();
        card.WeightKg.Should().BeNull();
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

    [Fact]
    public async Task GetProductCardsAsync_UnauthorizedError_DoesNotLogApiKey()
    {
        // Arrange
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl)
            .Respond(HttpStatusCode.Unauthorized, "application/json", "{\"error\": \"Invalid API key\"}");

        Mock<ILogger<WildberriesApiClient>> loggerMock = new Mock<ILogger<WildberriesApiClient>>();
        List<string> loggedMessages = new List<string>();

        // Capture all log messages
        loggerMock
            .Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback(new InvocationAction(invocation =>
            {
                LogLevel logLevel = (LogLevel)invocation.Arguments[0];
                object state = invocation.Arguments[2];
                Exception? exception = (Exception?)invocation.Arguments[3];
                Delegate formatter = (Delegate)invocation.Arguments[4];

                string message = formatter.DynamicInvoke(state, exception)?.ToString() ?? string.Empty;
                loggedMessages.Add(message);
            }));

        HttpClient typedClient = mockHttp.ToHttpClient();
        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(mockHttp.ToHttpClient());

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Content,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentBaseUrl);

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            factory.Object,
            endpointResolver.Object,
            loggerMock.Object);

        client.SetTemplate(TestTemplate);

        // Act
        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);

        // Verify that API key does not appear in any logged messages
        string sensitiveApiKey = "test-api-key";
        string partialApiKey = "test-api";

        foreach (string logMessage in loggedMessages)
        {
            logMessage.Should().NotContain(sensitiveApiKey,
                "API key should not appear in log messages");
            logMessage.Should().NotContain(partialApiKey,
                "Partial API key should not appear in log messages");
        }

        // Verify that some logging occurred (but without sensitive data)
        loggedMessages.Should().NotBeEmpty("Error should be logged");
    }

    [Fact]
    public async Task GetProductCardsAsync_ServerError_DoesNotLogApiKey()
    {
        // Arrange
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl)
            .Respond(HttpStatusCode.InternalServerError, "application/json", "{\"error\": \"Internal error\"}");

        Mock<ILogger<WildberriesApiClient>> loggerMock = new Mock<ILogger<WildberriesApiClient>>();
        List<string> loggedMessages = new List<string>();

        loggerMock
            .Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback(new InvocationAction(invocation =>
            {
                LogLevel logLevel = (LogLevel)invocation.Arguments[0];
                object state = invocation.Arguments[2];
                Exception? exception = (Exception?)invocation.Arguments[3];
                Delegate formatter = (Delegate)invocation.Arguments[4];

                string message = formatter.DynamicInvoke(state, exception)?.ToString() ?? string.Empty;
                loggedMessages.Add(message);
            }));

        HttpClient typedClient = mockHttp.ToHttpClient();
        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(mockHttp.ToHttpClient());

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Content,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentBaseUrl);

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            factory.Object,
            endpointResolver.Object,
            loggerMock.Object);

        client.SetTemplate(TestTemplate);

        // Act
        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(500);

        // Verify API key is not in logs
        string sensitiveApiKey = "test-api-key";

        foreach (string logMessage in loggedMessages)
        {
            logMessage.Should().NotContain(sensitiveApiKey,
                "API key must not leak into logs during server errors");
            logMessage.Should().NotContainAny(new[] { "Bearer ", "Authorization:" },
                "Authorization details should not appear in logs");
        }
    }

    [Fact]
    public async Task GetProductCardsAsync_NetworkFailure_DoesNotLogApiKey()
    {
        // Arrange
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl)
            .Throw(new HttpRequestException("Network unreachable"));

        Mock<ILogger<WildberriesApiClient>> loggerMock = new Mock<ILogger<WildberriesApiClient>>();
        List<string> loggedMessages = new List<string>();

        loggerMock
            .Setup(x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)))
            .Callback(new InvocationAction(invocation =>
            {
                LogLevel logLevel = (LogLevel)invocation.Arguments[0];
                object state = invocation.Arguments[2];
                Exception? exception = (Exception?)invocation.Arguments[3];
                Delegate formatter = (Delegate)invocation.Arguments[4];

                string message = formatter.DynamicInvoke(state, exception)?.ToString() ?? string.Empty;
                loggedMessages.Add(message);
            }));

        HttpClient typedClient = mockHttp.ToHttpClient();
        Mock<IHttpClientFactory> factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(mockHttp.ToHttpClient());

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Content,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentBaseUrl);

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            factory.Object,
            endpointResolver.Object,
            loggerMock.Object);

        client.SetTemplate(TestTemplate);

        // Act
        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();

        // Verify no API key in logs even during network failures
        string sensitiveApiKey = "test-api-key";

        foreach (string logMessage in loggedMessages)
        {
            logMessage.Should().NotContain(sensitiveApiKey,
                "API key must never leak into logs, even during exceptions");
        }
    }

    [Fact]
    public async Task GetProductCardsAsync_MixedCharacteristicValueTypes_DeserializesWholePage()
    {
        // Regression: WB sends a bare number for scalar characteristics ("Ширина предмета": 0.6)
        // while list characteristics stay string arrays. A List<string>-typed member cannot read
        // that payload, and the resulting JsonException failed the whole import job before the
        // first batch was ever published.
        string json = """
        {
            "cards": [
                {
                    "nmID": 622223778,
                    "imtID": 647746650,
                    "vendorCode": "свч-55",
                    "brand": "Свечно",
                    "title": "Свечи восковые",
                    "characteristics": [
                        { "id": 14177449, "name": "Цвет", "value": ["желтый", "черный"] },
                        { "id": 90673, "name": "Ширина предмета", "value": 0.6 },
                        { "id": 90630, "name": "Высота предмета", "value": 20 },
                        { "id": 89008, "name": "Вес товара без упаковки (г)", "value": 450 }
                    ],
                    "sizes": [
                        {
                            "chrtID": 844004713,
                            "techSize": "0",
                            "skus": ["2047352731808"]
                        }
                    ]
                }
            ],
            "cursor": {
                "updatedAt": "2026-09-25T10:27:51.323851Z",
                "nmID": 622223778,
                "total": 100
            }
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond("application/json", json);

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.ErrorMessage.Should().BeNull();
        result.Data!.Cards.Should().HaveCount(1);
        result.Data.TotalCount.Should().Be(100);

        WildberriesCardSnapshot card = result.Data.Cards.First();
        card.NmId.Should().Be(622223778);
        card.VendorCode.Should().Be("свч-55");
        card.Barcodes.Should().ContainSingle().Which.Should().Be("2047352731808");

        using JsonDocument channelData = JsonDocument.Parse(card.ChannelSpecificData!);
        JsonElement characteristics = channelData.RootElement.GetProperty("characteristics");
        characteristics.GetArrayLength().Should().Be(4);

        // Already-array values keep every element.
        characteristics[0].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("желтый", "черный");

        // Scalar numbers become single-element string arrays, invariant-culture formatted.
        characteristics[1].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("0.6");
        characteristics[2].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("20");
        characteristics[3].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("450");
    }

    [Fact]
    public async Task GetProductCardsAsync_ScalarCharacteristicValues_AreNormalizedToStringArrays()
    {
        string json = """
        {
            "cards": [
                {
                    "nmID": 777,
                    "vendorCode": "SCALARS",
                    "title": "Scalar values",
                    "characteristics": [
                        { "id": 1, "name": "Целое", "value": 42 },
                        { "id": 2, "name": "Дробное", "value": 1.5 },
                        { "id": 3, "name": "Строка", "value": "Россия" },
                        { "id": 4, "name": "Булево", "value": true },
                        { "id": 5, "name": "Смешанное", "value": ["а", 2, false] },
                        { "id": 6, "name": "Пусто", "value": null },
                        { "id": 7, "name": "Без значения" }
                    ]
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

        using JsonDocument channelData = JsonDocument.Parse(result.Data!.Cards.First().ChannelSpecificData!);
        JsonElement characteristics = channelData.RootElement.GetProperty("characteristics");

        characteristics[0].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("42");
        characteristics[1].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("1.5");
        characteristics[2].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("Россия");
        characteristics[3].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("True");
        characteristics[4].GetProperty("value").EnumerateArray()
            .Select(v => v.GetString()).Should().Equal("а", "2", "False");

        // A JSON null and a missing "value" member both leave the property at its default, and
        // MapToSnapshot writes nulls as-is, so both surface as "value": null.
        characteristics[5].GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
        characteristics[6].GetProperty("value").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task GetProductCardsAsync_InvalidJsonThenValidJson_RetriesAndSucceeds()
    {
        string validJson = """
        {
            "cards": [
                {
                    "nmID": 321,
                    "vendorCode": "RETRY-OK",
                    "title": "После ретрая"
                }
            ],
            "cursor": null
        }
        """;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        int requestNumber = 0;

        // MockHttp 7.x has no ThenRespond; the response function is invoked per request, so the
        // call number decides which body is returned.
        MockedRequest cardsRequest = mockHttp.When(HttpMethod.Post, GetCardsListUrl);
        cardsRequest.Respond(_ =>
        {
            requestNumber++;

            string body = requestNumber == 1 ? "{ invalid json" : validJson;

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        });

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Data!.Cards.Should().HaveCount(1);
        result.Data.Cards.First().VendorCode.Should().Be("RETRY-OK");

        mockHttp.GetMatchCount(cardsRequest).Should().Be(2,
            "a malformed body should be retried once");
    }

    [Fact]
    public async Task GetProductCardsAsync_InvalidJsonOnEveryAttempt_ReturnsFailureAfterMaxAttempts()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        MockedRequest cardsRequest = mockHttp.When(HttpMethod.Post, GetCardsListUrl);
        cardsRequest.Respond("application/json", "{ invalid json");

        WildberriesApiClient client = BuildClient(mockHttp);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Invalid JSON");
        result.Data!.Cards.Should().BeEmpty();

        mockHttp.GetMatchCount(cardsRequest).Should().Be(3,
            "three attempts are made before the page is declared unreadable");
    }

    [Fact]
    public async Task GetProductCardsAsync_Unauthorized_LogsResponseBody()
    {
        string errorBody = "{\"error\":\"Invalid API key format\"}";

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl)
            .Respond(HttpStatusCode.Unauthorized, "application/json", errorBody);

        Mock<ILogger<WildberriesApiClient>> loggerMock = new Mock<ILogger<WildberriesApiClient>>();
        List<string> loggedMessages = BuildLogCapture(loggerMock);

        WildberriesApiClient client = BuildClient(mockHttp, logger: loggerMock.Object);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(401);
        loggedMessages.Should().ContainMatch(
            $"*{errorBody}*",
            "the response body is the only place WB explains the credential problem");
    }

    [Fact]
    public async Task GetProductCardsAsync_Forbidden_LogsResponseBody()
    {
        string errorBody = "{\"error\":\"Token is not allowed for content API\"}";

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl)
            .Respond(HttpStatusCode.Forbidden, "application/json", errorBody);

        Mock<ILogger<WildberriesApiClient>> loggerMock = new Mock<ILogger<WildberriesApiClient>>();
        List<string> loggedMessages = BuildLogCapture(loggerMock);

        WildberriesApiClient client = BuildClient(mockHttp, logger: loggerMock.Object);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(403);
        loggedMessages.Should().ContainMatch(
            $"*{errorBody}*",
            "the response body is the only place WB explains the credential problem");
    }

    [Fact]
    public async Task GetProductCardsAsync_LargeErrorBody_TruncatesLoggedBody()
    {
        string longErrorBody = new string('x', 2000);

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl)
            .Respond(HttpStatusCode.InternalServerError, "application/json", longErrorBody);

        Mock<ILogger<WildberriesApiClient>> loggerMock = new Mock<ILogger<WildberriesApiClient>>();
        List<string> loggedMessages = BuildLogCapture(loggerMock);

        WildberriesApiClient client = BuildClient(mockHttp, logger: loggerMock.Object);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();

        string loggedBody = new string('x', 500);
        string untruncatedBody = new string('x', 501);

        loggedMessages.Should().ContainMatch($"*{loggedBody}*");
        loggedMessages.Should().NotContainMatch($"*{untruncatedBody}*");
    }
}
