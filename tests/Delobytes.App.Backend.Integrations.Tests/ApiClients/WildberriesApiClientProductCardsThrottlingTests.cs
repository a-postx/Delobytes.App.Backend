using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Wildberries;
using Delobytes.App.Backend.Integrations.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RichardSzalay.MockHttp;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests that product-card requests are paced by the shared Wildberries limiter: a 429 updates the
/// cards bucket, a success feeds it the response counters, and a slot the limiter refuses is
/// reported instead of the request being issued.
/// </summary>
public class WildberriesApiClientProductCardsThrottlingTests
{
    private const string ContentBaseUrl = "https://content-api.wildberries.ru";
    private const string GetCardsListUrl = "https://content-api.wildberries.ru/content/v2/get/cards/list";

    private static readonly Guid TestTemplateId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly SystemChannelTemplate TestTemplate = new SystemChannelTemplate
    {
        Id = TestTemplateId,
        Code = "wildberries",
        DisplayName = "Wildberries",
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static WildberriesApiClient BuildClient(
        MockHttpMessageHandler mockHttp, IChannelRateLimiter rateLimiter)
    {
        Mock<IEndpointResolver> endpointResolver = new Mock<IEndpointResolver>();
        endpointResolver
            .Setup(r => r.GetEndpointUrlAsync(
                TestTemplateId,
                ChannelEndpointType.Content,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ContentBaseUrl);

        WildberriesApiClient client = new WildberriesApiClient(
            mockHttp.ToHttpClient(),
            endpointResolver.Object,
            new FixedConnectionResolver(),
            rateLimiter,
            Options.Create(new WildberriesImportOptions()),
            NullLogger<WildberriesApiClient>.Instance,
            TimeProvider.System);

        client.SetTemplate(TestTemplate);

        return client;
    }

    private static WildberriesRateLimiter BuildRealLimiter(
        IWildberriesMethodThrottleRepository repository, WildberriesRateLimitOptions? options = null)
    {
        return new WildberriesRateLimiter(
            repository,
            Options.Create(options ?? new WildberriesRateLimitOptions()),
            NullLogger<WildberriesRateLimiter>.Instance,
            TimeProvider.System);
    }

    private static HttpResponseMessage CardsResponse(HttpStatusCode statusCode)
    {
        HttpResponseMessage response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                statusCode == HttpStatusCode.OK ? """{ "cards": [], "cursor": null }""" : "{}",
                Encoding.UTF8,
                "application/json"),
        };

        response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "100");
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
        response.Headers.TryAddWithoutValidation("x-request-id", "cards-req-id");

        return response;
    }

    [Fact]
    public async Task GetProductCardsAsync_Returns429_ReportsTheThrottleOnTheCardsBucket()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(_ =>
        {
            HttpResponseMessage response = CardsResponse(HttpStatusCode.TooManyRequests);
            response.Headers.TryAddWithoutValidation("x-ratelimit-reset", "579");
            response.Headers.TryAddWithoutValidation("x-ratelimit-retry", "39");

            return response;
        });

        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = BuildRealLimiter(
            repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60, BackoffPadSeconds = 5 });

        WildberriesApiClient client = BuildClient(mockHttp, limiter);

        DateTimeOffset before = DateTimeOffset.UtcNow;

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(
            Guid.NewGuid(), null, 50, CancellationToken.None);

        DateTimeOffset after = DateTimeOffset.UtcNow;

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(429);
        result.ErrorMessage.Should().Contain("Rate limit exceeded");

        string fingerprint = ApiKeyFingerprint.Compute(FixedConnectionResolver.ApiKey);

        WildberriesMethodThrottle? cardsRow = await repository.GetAsync(
            fingerprint, WildberriesApiMethodRegistry.ContentCardsList, CancellationToken.None);

        cardsRow.Should().NotBeNull("the 429 must teach the limiter the cards method's pace");
        cardsRow!.LastStatusCode.Should().Be(429);
        cardsRow.ObservedWindowSeconds.Should().Be(579);
        cardsRow.NextRequestAllowedAt.Should().BeOnOrAfter(before.AddSeconds(39 + 5));
        cardsRow.NextRequestAllowedAt.Should().BeOnOrBefore(after.AddSeconds(39 + 5 + 2));

        WildberriesMethodThrottle? ordersRow = await repository.GetAsync(
            fingerprint, WildberriesApiMethodRegistry.SupplierOrders, CancellationToken.None);

        ordersRow.Should().BeNull("a cards throttle must not create or touch the orders bucket");
    }

    [Fact]
    public async Task GetProductCardsAsync_Success_ReportsTheCountersOnTheCardsBucket()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(_ => CardsResponse(HttpStatusCode.OK));

        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = BuildRealLimiter(repository);
        WildberriesApiClient client = BuildClient(mockHttp, limiter);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(
            Guid.NewGuid(), null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();

        WildberriesMethodThrottle? cardsRow = await repository.GetAsync(
            ApiKeyFingerprint.Compute(FixedConnectionResolver.ApiKey),
            WildberriesApiMethodRegistry.ContentCardsList,
            CancellationToken.None);

        cardsRow.Should().NotBeNull();
        cardsRow!.LastStatusCode.Should().Be(200);
        cardsRow.LastLimit.Should().Be(100);
        cardsRow.LastRemaining.Should().Be(0);
    }

    [Fact]
    public async Task GetProductCardsAsync_SlotRefusedBeyondTheWaitTolerance_DoesNotIssueTheRequest()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(_ =>
        {
            httpCallCount++;

            return CardsResponse(HttpStatusCode.OK);
        });

        WildberriesApiClient client = BuildClient(mockHttp, new RefusingWildberriesRateLimiter());

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(
            Guid.NewGuid(), null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.StatusCode.Should().Be(429);
        result.ErrorMessage.Should().Contain("Rate limit");
        result.Data!.Cards.Should().BeEmpty();

        httpCallCount.Should().Be(0, "a slot the limiter has not released must not be spent on a request");
    }

    [Fact]
    public async Task GetProductCardsAsync_SlotRefusedWithinTheWaitTolerance_WaitsThenIssuesTheRequest()
    {
        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(_ => CardsResponse(HttpStatusCode.OK));

        DelayedFirstRefusalRateLimiter limiter = new(TimeSpan.FromMilliseconds(30));
        WildberriesApiClient client = BuildClient(mockHttp, limiter);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(
            Guid.NewGuid(), null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("the client waits out a short slot instead of failing the page");
        limiter.TryAcquireCalls.Should().Be(2);
    }

    [Fact]
    public async Task GetProductCardsAsync_SlotBecomesDueWhileChecking_RetriesInsteadOfFailing()
    {
        // Production regression: the limiter refused with a deadline that was still a fraction of
        // a millisecond ahead when it read the clock, and already in the past by the time the
        // client read it after one database round-trip. That is "the slot is due now", not
        // "wait an hour", and must not end the run.
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(_ =>
        {
            httpCallCount++;

            return CardsResponse(HttpStatusCode.OK);
        });

        StaleSlotRateLimiter limiter = new(refusalCount: 1, offset: TimeSpan.FromMilliseconds(-10));
        WildberriesApiClient client = BuildClient(mockHttp, limiter);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(
            Guid.NewGuid(), null, 50, CancellationToken.None);

        result.IsSuccess.Should().BeTrue("a refusal whose slot has already passed is re-asked, not reported");
        limiter.TryAcquireCalls.Should().Be(2);
        httpCallCount.Should().Be(1);
    }

    [Fact]
    public async Task GetProductCardsAsync_LimiterKeepsRefusingWithAPastSlot_GivesUpAfterBoundedRetries()
    {
        int httpCallCount = 0;

        MockHttpMessageHandler mockHttp = new MockHttpMessageHandler();
        mockHttp.When(HttpMethod.Post, GetCardsListUrl).Respond(_ =>
        {
            httpCallCount++;

            return CardsResponse(HttpStatusCode.OK);
        });

        StaleSlotRateLimiter limiter = new(refusalCount: int.MaxValue, offset: TimeSpan.FromMilliseconds(-10));
        WildberriesApiClient client = BuildClient(mockHttp, limiter);

        ApiResponse<ProductCardsData> result = await client.GetProductCardsAsync(
            Guid.NewGuid(), null, 50, CancellationToken.None);

        // A limiter that never stops claiming an already-due slot must not spin the importer
        // forever: the client gives up after a small, bounded number of immediate re-asks.
        result.IsSuccess.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Rate limit");
        limiter.TryAcquireCalls.Should().BeGreaterThan(1);
        limiter.TryAcquireCalls.Should().BeLessThanOrEqualTo(6);
        httpCallCount.Should().Be(0);
    }

    /// <summary>
    /// Refuses the first <c>refusalCount</c> slots with a deadline offset by <c>offset</c> (negative
    /// makes it already due), then grants every slot.
    /// </summary>
    private sealed class StaleSlotRateLimiter : IChannelRateLimiter
    {
        private readonly int _refusalCount;
        private readonly TimeSpan _offset;

        public StaleSlotRateLimiter(int refusalCount, TimeSpan offset)
        {
            _refusalCount = refusalCount;
            _offset = offset;
        }

        public int TryAcquireCalls { get; private set; }

        public Task<RateLimitAcquireResult> TryAcquireAsync(
            RateLimitScope scope, CancellationToken cancellationToken)
        {
            TryAcquireCalls++;

            return Task.FromResult(new RateLimitAcquireResult
            {
                Acquired = TryAcquireCalls > _refusalCount,
                NextRequestAllowedAt = DateTimeOffset.UtcNow.Add(_offset),
            });
        }

        public Task<DateTimeOffset> ReportRateLimitedAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task ReportSuccessAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
            RateLimitScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }
    }

    /// <summary>
    /// Refuses every slot with a next-allowed instant far beyond the in-process wait tolerance, so
    /// the client must report the refusal rather than sleep for an hour.
    /// </summary>
    private sealed class RefusingWildberriesRateLimiter : IChannelRateLimiter
    {
        public Task<RateLimitAcquireResult> TryAcquireAsync(
            RateLimitScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult(new RateLimitAcquireResult
            {
                Acquired = false,
                NextRequestAllowedAt = DateTimeOffset.UtcNow.AddHours(1),
            });
        }

        public Task<DateTimeOffset> ReportRateLimitedAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A refused slot cannot produce a 429 response.");
        }

        public Task ReportSuccessAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            throw new InvalidOperationException("A refused slot cannot produce a successful response.");
        }

        public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
            RateLimitScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTimeOffset?>(DateTimeOffset.UtcNow.AddHours(1));
        }
    }

    /// <summary>
    /// Refuses the first slot with a deadline inside the wait tolerance, then grants every slot.
    /// </summary>
    private sealed class DelayedFirstRefusalRateLimiter : IChannelRateLimiter
    {
        private readonly TimeSpan _delay;

        public DelayedFirstRefusalRateLimiter(TimeSpan delay)
        {
            _delay = delay;
        }

        public int TryAcquireCalls { get; private set; }

        public Task<RateLimitAcquireResult> TryAcquireAsync(
            RateLimitScope scope, CancellationToken cancellationToken)
        {
            TryAcquireCalls++;

            return Task.FromResult(new RateLimitAcquireResult
            {
                Acquired = TryAcquireCalls > 1,
                NextRequestAllowedAt = DateTimeOffset.UtcNow.Add(_delay),
            });
        }

        public Task<DateTimeOffset> ReportRateLimitedAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            return Task.FromResult(DateTimeOffset.UtcNow);
        }

        public Task ReportSuccessAsync(
            RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
            RateLimitScope scope, CancellationToken cancellationToken)
        {
            return Task.FromResult<DateTimeOffset?>(null);
        }
    }
}
