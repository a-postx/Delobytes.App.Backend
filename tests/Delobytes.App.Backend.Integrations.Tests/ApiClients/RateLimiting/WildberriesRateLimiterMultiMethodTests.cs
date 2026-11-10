using System;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using Delobytes.App.Backend.Integrations.Tests.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.RateLimiting;

/// <summary>
/// Tests that the single Wildberries limiter keeps a separate pacing bucket per (key, method).
/// Sharing one implementation must not collapse the buckets: Wildberries meters each method on its
/// own, so a 429 on product cards must not stop orders from being fetched on the same key.
/// </summary>
public class WildberriesRateLimiterMultiMethodTests
{
    private const string ApiKey = "wb-secret-key-multi-method";
    private const string ChannelCode = "wildberries";

    private static WildberriesRateLimiter CreateLimiter(
        FakeWildberriesMethodThrottleRepository repository,
        WildberriesRateLimitOptions? options = null)
    {
        return new WildberriesRateLimiter(
            repository,
            Options.Create(options ?? new WildberriesRateLimitOptions()),
            NullLogger<WildberriesRateLimiter>.Instance,
            TimeProvider.System);
    }

    private static RateLimitScope Scope(string method)
    {
        return new RateLimitScope(ChannelCode, ApiKey, method);
    }

    private static RateLimitSignal ThrottledSignal()
    {
        return new RateLimitSignal
        {
            Limit = 10,
            Remaining = 0,
            ResetSeconds = 579,
            RetrySeconds = 3600,
            RequestId = "req-429",
            StatusCode = 429,
        };
    }

    [Fact]
    public async Task TryAcquireAsync_SecondMethodOnTheSameKey_HasItsOwnAvailableSlot()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(
            repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });

        RateLimitAcquireResult orders = await limiter.TryAcquireAsync(
            Scope(WildberriesApiMethodRegistry.SupplierOrders), CancellationToken.None);

        RateLimitAcquireResult cards = await limiter.TryAcquireAsync(
            Scope(WildberriesApiMethodRegistry.ContentCardsList), CancellationToken.None);

        orders.Acquired.Should().BeTrue();
        cards.Acquired.Should().BeTrue("a method that has never been called has spent no slot of its own");
    }

    [Fact]
    public async Task TryAcquireAsync_SameMethodTwice_PacesWithinTheOneBucket()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(
            repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });

        RateLimitAcquireResult first = await limiter.TryAcquireAsync(
            Scope(WildberriesApiMethodRegistry.SupplierOrders), CancellationToken.None);

        RateLimitAcquireResult second = await limiter.TryAcquireAsync(
            Scope(WildberriesApiMethodRegistry.SupplierOrders), CancellationToken.None);

        first.Acquired.Should().BeTrue();
        second.Acquired.Should().BeFalse("the interval set by the first call belongs to the same bucket");
        second.NextRequestAllowedAt.Should().Be(first.NextRequestAllowedAt);
    }

    [Fact]
    public async Task ReportRateLimitedAsync_OnOneMethod_DoesNotDelayTheOtherMethodsOfTheSameKey()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(
            repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60, BackoffPadSeconds = 5 });

        DateTimeOffset ordersRetryAt = await limiter.ReportRateLimitedAsync(
            Scope(WildberriesApiMethodRegistry.SupplierOrders), ThrottledSignal(), CancellationToken.None);

        DateTimeOffset? cardsNextAllowedAt = await limiter.GetNextRequestAllowedAtAsync(
            Scope(WildberriesApiMethodRegistry.ContentCardsList), CancellationToken.None);

        RateLimitAcquireResult cards = await limiter.TryAcquireAsync(
            Scope(WildberriesApiMethodRegistry.ContentCardsList), CancellationToken.None);

        ordersRetryAt.Should().BeAfter(DateTimeOffset.UtcNow, "the throttled method must wait");
        cardsNextAllowedAt.Should().BeNull("the untouched method has no pacing row at all");
        cards.Acquired.Should().BeTrue("a 429 on orders says nothing about the allowance for cards");
    }

    [Fact]
    public async Task TryAcquireAsync_MethodSpecificPolicy_StartsAtThatMethodInterval()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(
            repository,
            new WildberriesRateLimitOptions
            {
                InitialIntervalSeconds = 60,
                Methods =
                {
                    [WildberriesApiMethodRegistry.ContentCardsList] =
                        new WildberriesRateLimitOptions.MethodRateLimitPolicy { InitialIntervalSeconds = 10 },
                },
            });

        await limiter.TryAcquireAsync(
            Scope(WildberriesApiMethodRegistry.ContentCardsList), CancellationToken.None);

        WildberriesMethodThrottle? row = await repository.GetAsync(
            ApiKeyFingerprint.Compute(ApiKey),
            WildberriesApiMethodRegistry.ContentCardsList,
            CancellationToken.None);

        row.Should().NotBeNull();
        row!.CurrentIntervalSeconds.Should().Be(10, "the method's own policy outranks the root default");
    }

    [Fact]
    public async Task ThrottleRows_AreStoredPerMethodForTheSameKey()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository);
        string fingerprint = ApiKeyFingerprint.Compute(ApiKey);

        await limiter.ReportSuccessAsync(
            Scope(WildberriesApiMethodRegistry.SupplierOrders),
            new RateLimitSignal { Limit = 10, Remaining = 9, StatusCode = 200 },
            CancellationToken.None);

        await limiter.ReportSuccessAsync(
            Scope(WildberriesApiMethodRegistry.ContentCardsList),
            new RateLimitSignal { Limit = 100, Remaining = 99, StatusCode = 200 },
            CancellationToken.None);

        WildberriesMethodThrottle? ordersRow = await repository.GetAsync(
            fingerprint, WildberriesApiMethodRegistry.SupplierOrders, CancellationToken.None);

        WildberriesMethodThrottle? cardsRow = await repository.GetAsync(
            fingerprint, WildberriesApiMethodRegistry.ContentCardsList, CancellationToken.None);

        ordersRow.Should().NotBeNull();
        cardsRow.Should().NotBeNull();
        ordersRow!.Method.Should().Be(WildberriesApiMethodRegistry.SupplierOrders);
        cardsRow!.Method.Should().Be(WildberriesApiMethodRegistry.ContentCardsList);
        ordersRow.LastLimit.Should().Be(10);
        cardsRow.LastLimit.Should().Be(100);
    }
}
