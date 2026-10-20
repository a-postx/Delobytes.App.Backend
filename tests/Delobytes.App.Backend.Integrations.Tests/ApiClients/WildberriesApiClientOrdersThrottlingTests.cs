using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using Delobytes.App.Backend.Integrations.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for the throttle gate, 429 handling and the multi-day flag=1 guard inside
/// <see cref="WildberriesApiClient.GetOrdersAsync"/>, wired to the real
/// <see cref="WildberriesRateLimiter"/> rather than the always-allow double that the
/// mapping-focused tests in <c>WildberriesApiClientGetOrdersTests</c> use.
/// </summary>
public class WildberriesApiClientOrdersThrottlingTests
{
    private const string StatisticsBaseUrl = "https://statistics-api.wildberries.ru";

    private static readonly Guid TestTemplateId = Guid.Parse("22222222-2222-2222-2222-222222222222");
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
        IWildberriesRateLimiter rateLimiter,
        ILogger<WildberriesApiClient>? logger = null)
    {
        HttpClient typedClient = mockHttp.ToHttpClient();

        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Statistics,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(StatisticsBaseUrl);

        WildberriesApiClient client = new WildberriesApiClient(
            typedClient,
            endpointResolver.Object,
            new FixedConnectionResolver(),
            rateLimiter,
            Options.Create(new WildberriesImportOptions()),
            logger ?? NullLogger<WildberriesApiClient>.Instance);

        client.SetTemplate(TestTemplate);

        return client;
    }

    private static WildberriesRateLimiter BuildRealLimiter(
        IWildberriesMethodThrottleRepository repository, WildberriesRateLimitOptions? options = null)
    {
        return new WildberriesRateLimiter(
            repository,
            Options.Create(options ?? new WildberriesRateLimitOptions()),
            NullLogger<WildberriesRateLimiter>.Instance);
    }

    private static OrdersRequest BuildDayRequest(DateTimeOffset day)
    {
        return new OrdersRequest
        {
            DateFrom = day,
            DateTo = day.AddDays(1).AddSeconds(-1),
            Flag = 1,
            Limit = 0,
        };
    }

    private static List<string> CaptureLogMessages(Mock<ILogger<WildberriesApiClient>> loggerMock)
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
    public async Task GetOrdersAsync_SecondConsecutiveCallBeforeIntervalElapses_ReturnsThrottledWithoutIssuingHttpRequest()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond(_ =>
            {
                httpCallCount++;

                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                };
                response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "10");
                response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "9");

                return response;
            });

        WildberriesRateLimiter rateLimiter = BuildRealLimiter(
            new FakeWildberriesMethodThrottleRepository(), new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });

        WildberriesApiClient client = BuildClient(mockHttp, rateLimiter);

        OrdersRequest request = BuildDayRequest(new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.FromHours(3)));

        OrdersPage first = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);
        OrdersPage second = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        first.Throttled.Should().BeFalse();
        first.RetryAt.Should().BeNull();
        second.Throttled.Should().BeTrue();
        second.RetryAt.Should().NotBeNull("a refused slot must carry the newly reserved next slot");
        second.Orders.Should().BeEmpty();
        second.HasMore.Should().BeFalse();

        httpCallCount.Should().Be(1, "a refused slot must not issue a second HTTP request");
    }

    [Fact]
    public async Task GetOrdersAsync_Returns429WithRealCaptureHeaders_IsThrottledNotThrownAndHonoursRetryOverReset()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond(_ =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent(
                        "{\"title\":\"Too many requests\",\"status\":429}",
                        Encoding.UTF8,
                        "application/problem+json"),
                };
                response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "10");
                response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
                response.Headers.TryAddWithoutValidation("x-ratelimit-reset", "579");
                response.Headers.TryAddWithoutValidation("x-ratelimit-retry", "39");
                response.Headers.TryAddWithoutValidation("x-request-id", "fcdda24eaf40d3b4a1273b3065b71c5c");

                return response;
            });

        FakeWildberriesMethodThrottleRepository repository = new FakeWildberriesMethodThrottleRepository();
        Mock<ILogger<WildberriesApiClient>> clientLogger = new Mock<ILogger<WildberriesApiClient>>();
        List<string> loggedMessages = CaptureLogMessages(clientLogger);

        WildberriesRateLimiter rateLimiter = BuildRealLimiter(
            repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60, BackoffPadSeconds = 5 });

        WildberriesApiClient client = BuildClient(mockHttp, rateLimiter, clientLogger.Object);

        OrdersRequest request = BuildDayRequest(new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.FromHours(3)));

        DateTimeOffset before = DateTimeOffset.UtcNow;

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.Throttled.Should().BeTrue();
        page.Orders.Should().BeEmpty();

        WildberriesMethodThrottle? row = await repository.GetAsync(
            WildberriesKeyFingerprint.Compute(FixedConnectionResolver.ApiKey),
            WildberriesApiMethod.SupplierOrders,
            CancellationToken.None);

        row.Should().NotBeNull();

        page.RetryAt.Should().NotBeNull();
        page.RetryAt.Should().Be(row!.NextRequestAllowedAt, "the 429 path's RetryAt is exactly what ReportRateLimitedAsync computed");

        TimeSpan waitFromNow = row.NextRequestAllowedAt - before;
        waitFromNow.TotalSeconds.Should().BeApproximately(45, 2, "retry=39s + 5s pad, not reset=579s");

        loggedMessages.Should().Contain(message => message.Contains("fcdda24eaf40d3b4a1273b3065b71c5c"));
    }

    [Fact]
    public async Task GetOrdersAsync_SuccessWithOnlyLimitAndRemaining_ReportsSuccessWithoutThrowingOrDerivingInterval()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond(_ =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                };
                response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "10");
                response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "9");
                response.Headers.TryAddWithoutValidation("x-request-id", "4b576135a03dfb1d278930bc40fcd0e7");

                return response;
            });

        FakeWildberriesMethodThrottleRepository repository = new FakeWildberriesMethodThrottleRepository();
        WildberriesRateLimiter rateLimiter = BuildRealLimiter(repository);
        WildberriesApiClient client = BuildClient(mockHttp, rateLimiter);

        OrdersRequest request = BuildDayRequest(new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.FromHours(3)));

        Func<Task> act = () => client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().NotThrowAsync();

        WildberriesMethodThrottle? row = await repository.GetAsync(
            WildberriesKeyFingerprint.Compute(FixedConnectionResolver.ApiKey),
            WildberriesApiMethod.SupplierOrders,
            CancellationToken.None);

        row.Should().NotBeNull();
        row!.ObservedWindowSeconds.Should().BeNull("a 200 never carries reset; deriving an interval from it would be wrong");
        row.LastLimit.Should().Be(10);
        row.LastRemaining.Should().Be(9);
    }

    [Fact]
    public async Task GetOrdersAsync_SuccessWithRemainingZero_DoesNotStallTheRun()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond(_ =>
            {
                HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                };
                response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "10");
                response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");

                return response;
            });

        WildberriesRateLimiter rateLimiter = BuildRealLimiter(new FakeWildberriesMethodThrottleRepository());
        WildberriesApiClient client = BuildClient(mockHttp, rateLimiter);

        OrdersRequest request = BuildDayRequest(new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.FromHours(3)));

        OrdersPage page = await client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        page.Throttled.Should().BeFalse("remaining=0 on a 200 is not an error; the day was still served");
        page.Orders.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task GetOrdersAsync_MultiDayFlagOneWindow_ThrowsBeforeConsumingAThrottleSlot()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond(_ =>
            {
                httpCallCount++;

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("[]", Encoding.UTF8, "application/json"),
                };
            });

        WildberriesRateLimiter rateLimiter = BuildRealLimiter(new FakeWildberriesMethodThrottleRepository());
        WildberriesApiClient client = BuildClient(mockHttp, rateLimiter);

        OrdersRequest request = new OrdersRequest
        {
            DateFrom = new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.FromHours(3)),
            DateTo = new DateTimeOffset(2024, 1, 11, 23, 59, 59, TimeSpan.FromHours(3)),
            Flag = 1,
            Limit = 0,
        };

        Func<Task> act = () => client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();

        httpCallCount.Should().Be(0, "a rejected request must not consume a throttle slot or reach the transport");
    }

    [Fact]
    public async Task GetOrdersAsync_SingleDayFlagOneWindow_DoesNotThrow()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp
            .When(HttpMethod.Get, $"{StatisticsBaseUrl}/api/v1/supplier/orders")
            .Respond(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[]", Encoding.UTF8, "application/json"),
            });

        WildberriesRateLimiter rateLimiter = BuildRealLimiter(new FakeWildberriesMethodThrottleRepository());
        WildberriesApiClient client = BuildClient(mockHttp, rateLimiter);

        OrdersRequest request = BuildDayRequest(new DateTimeOffset(2024, 1, 10, 0, 0, 0, TimeSpan.FromHours(3)));

        Func<Task> act = () => client.GetOrdersAsync(Guid.NewGuid(), request, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
