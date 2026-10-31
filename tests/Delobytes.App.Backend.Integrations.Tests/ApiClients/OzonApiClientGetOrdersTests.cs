using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for <see cref="OzonApiClient.GetOrdersAsync"/>: one FBO page per request, request shape,
/// connection-bound credentials, cursor propagation and throttle handling.
/// </summary>
public class OzonApiClientGetOrdersTests
{
    private const string ApiBaseUrl = "https://api-seller.ozon.ru";
    private const string FboListPath = "/v3/posting/fbo/list";
    private const string FboListUrl = ApiBaseUrl + FboListPath;

    private static readonly DateTimeOffset WindowFrom = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset WindowTo = new DateTimeOffset(2024, 3, 1, 23, 59, 59, TimeSpan.Zero);

    private static SystemChannelTemplate BuildTemplate()
    {
        return new SystemChannelTemplate
        {
            Id = Guid.NewGuid(),
            Code = "ozon",
            DisplayName = "Ozon",
            ApiBaseUrl = ApiBaseUrl,
            ApiVersion = "v3",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Connection BuildConnection(Guid connectionId, string apiKey, string sellerId)
    {
        return new Connection
        {
            Id = connectionId,
            ChannelId = Guid.NewGuid(),
            Name = "Ozon",
            ApiKey = apiKey,
            Settings = JsonSerializer.Serialize(new { sellerId }),
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };
    }

    private static Mock<IConnectionResolver> BuildResolver(params Connection[] connections)
    {
        Mock<IConnectionResolver> resolver = new Mock<IConnectionResolver>();

        resolver
            .Setup(r => r.ResolveByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => connections.Single(c => c.Id == id));

        resolver
            .Setup(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(
                "Orders requests must resolve the connection by id, not by channel code."));

        return resolver;
    }

    private static OzonApiClient BuildClient(
        MockHttpMessageHandler mockHttp,
        IConnectionResolver resolver,
        IOrdersRateLimiter rateLimiter)
    {
        // The real auth handler is kept in the pipeline: the credentials on the wire are what the
        // stage is about, so a stub that injects headers would prove nothing.
        OzonAuthHandler authHandler = new OzonAuthHandler(
            resolver, NullLogger<OzonAuthHandler>.Instance)
        {
            InnerHandler = mockHttp,
        };

        OzonApiClient client = new OzonApiClient(
            new HttpClient(authHandler),
            Mock.Of<IHttpClientFactory>(),
            resolver,
            rateLimiter,
            NullLogger<OzonApiClient>.Instance);

        client.SetTemplate(BuildTemplate());

        return client;
    }

    private static OrdersRequest BuildRequest(string? cursor = null, int limit = 50)
    {
        return new OrdersRequest
        {
            DateFrom = WindowFrom,
            DateTo = WindowTo,
            Selection = OrdersSelectionMode.LastChanged,
            Limit = limit,
            Cursor = cursor,
            Schema = OrdersSchema.Fbo,
        };
    }

    private static string BuildEmptyPageBody(bool hasNext = false, string? cursor = null)
    {
        return JsonSerializer.Serialize(new
        {
            has_next = hasNext,
            cursor,
            postings = Array.Empty<object>(),
        });
    }

    private static string BuildPostingJson(string postingNumber, string orderNumber)
    {
        return $$"""
        {
          "posting_number": "{{postingNumber}}",
          "order_id": 1001,
          "order_number": "{{orderNumber}}",
          "status": "awaiting_packaging",
          "substatus": "posting_awaiting_passport_data",
          "created_at": "2024-03-01T08:15:00Z",
          "in_process_at": "2024-03-01T08:20:00Z",
          "products": [
            { "sku": 555, "offer_id": "SKU-1", "name": "Item", "quantity": 2,
              "price": { "amount": "499.00", "currency": "RUB" } }
          ],
          "analytics_data": { "city": "Москва", "region": "Москва", "warehouse_name": "Хоругвино" },
          "financial_data": { "posting_services": { "marketplace_service_item_deliv_to_customer": 0 } }
        }
        """;
    }

    private static string BuildOnePostingPageBody(bool hasNext, string cursor)
    {
        return $$"""
        {
          "has_next": {{(hasNext ? "true" : "false")}},
          "cursor": "{{cursor}}",
          "postings": [ {{BuildPostingJson("posting-1", "order-1")}} ]
        }
        """;
    }

    private static OrdersPage RespondWithPage(
        MockHttpMessageHandler mockHttp, string body, Action<HttpRequestMessage, string>? inspect = null)
    {
        mockHttp
            .When(HttpMethod.Post, FboListUrl)
            .Respond(request =>
            {
                string requestBody = request.Content == null
                    ? string.Empty
                    : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                inspect?.Invoke(request, requestBody);

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                };
            });

        return new OrdersPage();
    }

    [Fact]
    public async Task GetOrdersAsync_IssuesOneFboPostWithTheDocumentedBodyShape()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        string? capturedBody = null;
        string? capturedMethod = null;

        RespondWithPage(mockHttp, BuildEmptyPageBody(), (request, body) =>
        {
            capturedBody = body;
            capturedMethod = request.Method.Method;
        });

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        await client.GetOrdersAsync(connectionId, BuildRequest(limit: 50), CancellationToken.None);

        capturedMethod.Should().Be("POST");
        capturedBody.Should().NotBeNull();

        using JsonDocument document = JsonDocument.Parse(capturedBody!);
        JsonElement root = document.RootElement;

        root.GetProperty("limit").GetInt32().Should().Be(50);
        root.GetProperty("sort_dir").GetString().Should().Be("ASC");
        root.GetProperty("translit").GetBoolean().Should().BeFalse();
        root.GetProperty("cursor").GetString().Should().BeEmpty();

        JsonElement with = root.GetProperty("with");
        with.GetProperty("analytics_data").GetBoolean().Should().BeTrue();
        with.GetProperty("financial_data").GetBoolean().Should().BeTrue();
        with.GetProperty("legal_info").GetBoolean().Should().BeFalse();

        JsonElement filter = root.GetProperty("filter");
        filter.GetProperty("since").GetString().Should().Be("2024-03-01T00:00:00Z");
        filter.GetProperty("to").GetString().Should().Be("2024-03-01T23:59:59Z");

        filter.GetProperty("statuses").EnumerateArray().Select(s => s.GetString())
            .Should().BeEquivalentTo(new[]
            {
                "awaiting_packaging",
                "awaiting_deliver",
                "delivering",
                "delivered",
                "cancelled",
            });

        mockHttp.GetMatchCount(mockHttp.Fallback).Should().Be(0);
    }

    [Fact]
    public async Task GetOrdersAsync_NonUtcWindow_SendsUtcZTimestamps()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        string? capturedBody = null;

        RespondWithPage(mockHttp, BuildEmptyPageBody(), (_, body) => capturedBody = body);

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        OrdersRequest request = BuildRequest();
        request.DateFrom = new DateTimeOffset(2024, 3, 1, 3, 0, 0, TimeSpan.FromHours(3));
        request.DateTo = new DateTimeOffset(2024, 3, 2, 2, 59, 59, TimeSpan.FromHours(3));

        await client.GetOrdersAsync(connectionId, request, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(capturedBody!);
        JsonElement filter = document.RootElement.GetProperty("filter");

        filter.GetProperty("since").GetString().Should().Be("2024-03-01T00:00:00Z");
        filter.GetProperty("to").GetString().Should().Be("2024-03-01T23:59:59Z");
    }

    [Fact]
    public async Task GetOrdersAsync_ExplicitStatuses_ArePassedThroughInsteadOfTheDefaults()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        string? capturedBody = null;

        RespondWithPage(mockHttp, BuildEmptyPageBody(), (_, body) => capturedBody = body);

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        OrdersRequest request = BuildRequest();
        request.Statuses = new List<string> { "delivered" };

        await client.GetOrdersAsync(connectionId, request, CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(capturedBody!);

        document.RootElement.GetProperty("filter").GetProperty("statuses")
            .EnumerateArray().Select(s => s.GetString())
            .Should().BeEquivalentTo(new[] { "delivered" });
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(-5, 100)]
    [InlineData(1, 1)]
    [InlineData(101, 100)]
    [InlineData(1000, 100)]
    public async Task GetOrdersAsync_LimitOutsideTheApiRange_IsClamped(int requested, int expected)
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        string? capturedBody = null;

        RespondWithPage(mockHttp, BuildEmptyPageBody(), (_, body) => capturedBody = body);

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        await client.GetOrdersAsync(connectionId, BuildRequest(limit: requested), CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(capturedBody!);

        document.RootElement.GetProperty("limit").GetInt32().Should().Be(expected);
    }

    [Fact]
    public async Task GetOrdersAsync_TwoActiveOzonConnections_UsesTheRequestedConnectionCredentials()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        string? clientId = null;
        string? apiKey = null;

        RespondWithPage(mockHttp, BuildEmptyPageBody(), (request, _) =>
        {
            clientId = request.Headers.TryGetValues("Client-Id", out IEnumerable<string>? ids)
                ? ids.Single()
                : null;
            apiKey = request.Headers.TryGetValues("Api-Key", out IEnumerable<string>? keys)
                ? keys.Single()
                : null;
        });

        Guid firstId = Guid.NewGuid();
        Guid secondId = Guid.NewGuid();

        Mock<IConnectionResolver> resolver = BuildResolver(
            BuildConnection(firstId, "key-first", "seller-first"),
            BuildConnection(secondId, "key-second", "seller-second"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        await client.GetOrdersAsync(secondId, BuildRequest(), CancellationToken.None);

        clientId.Should().Be("seller-second");
        apiKey.Should().Be("key-second");
    }

    [Fact]
    public async Task GetOrdersAsync_HasNextAndCursor_PropagateToThePage()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        RespondWithPage(mockHttp, BuildOnePostingPageBody(hasNext: true, cursor: "next-cursor-token"));

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        OrdersPage page = await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        page.HasMore.Should().BeTrue();
        page.NextCursor.Should().Be("next-cursor-token");
        page.Throttled.Should().BeFalse();
        page.RowsWithoutIdentity.Should().Be(0);
        page.Orders.Should().HaveCount(1);
        page.Orders[0].ExternalOrderId.Should().Be("order-1");
    }

    [Fact]
    public async Task GetOrdersAsync_RequestCursor_IsSentAsTheCursorOfTheNextRequest()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        string? capturedBody = null;

        RespondWithPage(mockHttp, BuildEmptyPageBody(), (_, body) => capturedBody = body);

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        await client.GetOrdersAsync(
            connectionId, BuildRequest(cursor: "cursor-from-previous-page"), CancellationToken.None);

        using JsonDocument document = JsonDocument.Parse(capturedBody!);

        document.RootElement.GetProperty("cursor").GetString().Should().Be("cursor-from-previous-page");
    }

    [Fact]
    public async Task GetOrdersAsync_PostingWithoutOrderNumber_FallsBackToOrderIdAndCountsNoLoss()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        string body = $$"""
        {
          "has_next": false,
          "cursor": "",
          "postings": [
            {
              "posting_number": "posting-9",
              "order_id": 4242,
              "status": "delivered",
              "created_at": "2024-03-01T09:00:00Z",
              "products": [ { "sku": 1, "offer_id": "A", "name": "A", "quantity": 1,
                              "price": { "amount": "10.00", "currency": "RUB" } } ]
            }
          ]
        }
        """;

        RespondWithPage(mockHttp, body);

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        OrdersPage page = await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        page.Orders.Should().HaveCount(1);
        page.Orders[0].ExternalOrderId.Should().Be("4242");
        page.RowsWithoutIdentity.Should().Be(0);
    }

    [Fact]
    public async Task GetOrdersAsync_PostingWithoutIdentity_IsCountedInRowsWithoutIdentity()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        string body = $$"""
        {
          "has_next": false,
          "cursor": "",
          "postings": [
            {
              "posting_number": "",
              "order_id": 0,
              "status": "delivered",
              "created_at": "2024-03-01T09:00:00Z",
              "products": [ { "sku": 1, "offer_id": "A", "name": "A", "quantity": 1,
                              "price": { "amount": "10.00", "currency": "RUB" } } ]
            }
          ]
        }
        """;

        RespondWithPage(mockHttp, body);

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        OrdersPage page = await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        page.Orders.Should().BeEmpty();
        page.RowsWithoutIdentity.Should().Be(1);
    }

    [Fact]
    public async Task GetOrdersAsync_429WithRetryAfterFiveSeconds_ReturnsThrottledPageWithoutThrowing()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        mockHttp
            .When(HttpMethod.Post, FboListUrl)
            .Respond(_ =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(
                        "{\"code\":8,\"message\":\"Too many requests\"}", Encoding.UTF8, "application/json"),
                };
                response.Headers.TryAddWithoutValidation("Retry-After", "5");
                response.Headers.TryAddWithoutValidation("Ratelimit-Remaining", "0");
                response.Headers.TryAddWithoutValidation("x-request-id", "req-429");

                return response;
            });

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        Mock<IOrdersRateLimiter> rateLimiter = new Mock<IOrdersRateLimiter>();
        rateLimiter
            .Setup(r => r.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult
            {
                Acquired = true,
                NextRequestAllowedAt = DateTimeOffset.UtcNow,
            });

        DateTimeOffset expectedRetryAt = DateTimeOffset.UtcNow.AddSeconds(5);
        RateLimitSignal? reportedSignal = null;

        rateLimiter
            .Setup(r => r.ReportRateLimitedAsync(
                It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()))
            .Callback<RateLimitScope, RateLimitSignal, CancellationToken>(
                (_, signal, _) => reportedSignal = signal)
            .ReturnsAsync(expectedRetryAt);

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, rateLimiter.Object);

        DateTimeOffset before = DateTimeOffset.UtcNow;
        OrdersPage page = await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        page.Throttled.Should().BeTrue();
        page.HasMore.Should().BeFalse();
        page.Orders.Should().BeEmpty();
        page.RetryAt.Should().Be(expectedRetryAt);

        reportedSignal.Should().NotBeNull();
        reportedSignal!.RetrySeconds.Should().Be(5);
        reportedSignal.Remaining.Should().Be(0);
        reportedSignal.RequestId.Should().Be("req-429");

        // The client must not invent the wait itself: it hands the server's advice to the limiter
        // and reports whatever instant the limiter computed.
        page.RetryAt!.Value.Should().BeCloseTo(before.AddSeconds(5), TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task GetOrdersAsync_NonSuccessResponse_ThrowsHttpRequestExceptionCarryingTheStatus()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();

        mockHttp
            .When(HttpMethod.Post, FboListUrl)
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
            {
                Content = new StringContent(
                    "{\"code\":3,\"message\":\"PERIOD_IS_TOO_LONG\"}", Encoding.UTF8, "application/json"),
            });

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        Func<Task> act = async () =>
            await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        HttpRequestException exception = (await act.Should().ThrowAsync<HttpRequestException>()).Which;

        exception.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOrdersAsync_LimiterRefusal_ReturnsThrottledPageWithoutIssuingHttpRequest()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, FboListUrl)
            .Respond(_ =>
            {
                httpCallCount++;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(BuildEmptyPageBody(), Encoding.UTF8, "application/json"),
                };
            });

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        DateTimeOffset nextSlot = DateTimeOffset.UtcNow.AddSeconds(2);

        Mock<IOrdersRateLimiter> rateLimiter = new Mock<IOrdersRateLimiter>();
        rateLimiter
            .Setup(r => r.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult { Acquired = false, NextRequestAllowedAt = nextSlot });

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, rateLimiter.Object);

        OrdersPage page = await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        page.Throttled.Should().BeTrue();
        page.RetryAt.Should().Be(nextSlot);
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        page.Orders.Should().BeEmpty();
        httpCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrdersAsync_LimiterScope_IsKeyedByChannelMethodAndResolvedApiKey()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        RespondWithPage(mockHttp, BuildEmptyPageBody());

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-of-this-connection", "seller-1"));

        Mock<IOrdersRateLimiter> rateLimiter = new Mock<IOrdersRateLimiter>();
        rateLimiter
            .Setup(r => r.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult
            {
                Acquired = true,
                NextRequestAllowedAt = DateTimeOffset.UtcNow,
            });

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, rateLimiter.Object);

        await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        rateLimiter.Verify(
            r => r.TryAcquireAsync(
                It.Is<RateLimitScope>(scope =>
                    scope.ChannelCode == "ozon"
                    && scope.ApiKey == "key-of-this-connection"
                    && scope.Method == OrdersRateLimiterMethods.OzonPostingFboList),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetOrdersAsync_FbsSchema_ThrowsNotSupportedAndIssuesNoRequest()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, FboListUrl)
            .Respond(_ =>
            {
                httpCallCount++;

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        OrdersRequest request = BuildRequest();
        request.Schema = OrdersSchema.Fbs;

        Func<Task> act = async () => await client.GetOrdersAsync(connectionId, request, CancellationToken.None);

        NotSupportedException exception =
            (await act.Should().ThrowAsync<NotSupportedException>()).Which;

        exception.Message.Should().Contain("FBS");
        httpCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrdersAsync_InvertedWindow_ThrowsArgumentAndIssuesNoRequest()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Post, FboListUrl)
            .Respond(_ =>
            {
                httpCallCount++;

                return new HttpResponseMessage(HttpStatusCode.OK);
            });

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, new AlwaysAllowOrdersRateLimiter());

        OrdersRequest request = BuildRequest();
        request.DateFrom = WindowTo;
        request.DateTo = WindowFrom;

        Func<Task> act = async () => await client.GetOrdersAsync(connectionId, request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
        httpCallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetOrdersAsync_SuccessResponse_ReportsSuccessBeforeReadingTheBody()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        RespondWithPage(mockHttp, BuildEmptyPageBody());

        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        Mock<IOrdersRateLimiter> rateLimiter = new Mock<IOrdersRateLimiter>();
        rateLimiter
            .Setup(r => r.TryAcquireAsync(It.IsAny<RateLimitScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult
            {
                Acquired = true,
                NextRequestAllowedAt = DateTimeOffset.UtcNow,
            });

        OzonApiClient client = BuildClient(mockHttp, resolver.Object, rateLimiter.Object);

        await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        rateLimiter.Verify(
            r => r.ReportSuccessAsync(
                It.Is<RateLimitScope>(scope => scope.ChannelCode == "ozon"),
                It.IsAny<RateLimitSignal>(),
                It.IsAny<CancellationToken>()),
            Times.Once);

        rateLimiter.Verify(
            r => r.ReportRateLimitedAsync(
                It.IsAny<RateLimitScope>(), It.IsAny<RateLimitSignal>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetOrdersAsync_NoTemplateBound_ThrowsInvalidOperation()
    {
        Guid connectionId = Guid.NewGuid();
        Mock<IConnectionResolver> resolver =
            BuildResolver(BuildConnection(connectionId, "key-1", "seller-1"));

        OzonApiClient client = new OzonApiClient(
            new HttpClient(new MockHttpMessageHandler()),
            Mock.Of<IHttpClientFactory>(),
            resolver.Object,
            new AlwaysAllowOrdersRateLimiter(),
            NullLogger<OzonApiClient>.Instance);

        Func<Task> act = async () =>
            await client.GetOrdersAsync(connectionId, BuildRequest(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
