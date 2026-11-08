using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.RateLimiting;

/// <summary>
/// Tests for <see cref="OzonRateLimiter"/>: the per-<c>ApiKey</c> token bucket pacing Ozon orders
/// requests, specified in Stage 4 of the Ozon orders import specification.
/// </summary>
public class OzonRateLimiterTests
{
    private const string Method = OrdersRateLimiterMethods.OzonPostingFboList;

    private static RateLimitScope Scope(string apiKey)
    {
        return new RateLimitScope("ozon", apiKey, Method);
    }

    // These tests cover the token bucket and the penalty window, so the minimum-interval
    // throttle is disabled: TimeSpan.Zero means "no minimum interval" in OzonRateLimiter.
    private static OzonRateLimiter CreateLimiter(TimeProvider timeProvider)
    {
        return new OzonRateLimiter(timeProvider, TimeSpan.Zero);
    }

    [Fact]
    public async Task TryAcquireAsync_FiftyAcquisitionsInOneSecond_SucceedAndTheFiftyFirstIsRefusedWithAFutureInstant()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);
        RateLimitScope scope = Scope("key-a");

        for (int i = 0; i < 50; i++)
        {
            RateLimitAcquireResult result = await limiter.TryAcquireAsync(scope, CancellationToken.None);
            result.Acquired.Should().BeTrue($"acquisition {i + 1} of 50 should fit the full bucket");
        }

        RateLimitAcquireResult fiftyFirst = await limiter.TryAcquireAsync(scope, CancellationToken.None);

        fiftyFirst.Acquired.Should().BeFalse();
        fiftyFirst.NextRequestAllowedAt.Should().BeAfter(timeProvider.GetUtcNow());
    }

    [Fact]
    public async Task TryAcquireAsync_AfterSecondBoundary_BucketRefillsAndAcquisitionsSucceedAgain()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);
        RateLimitScope scope = Scope("key-a");

        for (int i = 0; i < 50; i++)
        {
            await limiter.TryAcquireAsync(scope, CancellationToken.None);
        }

        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromSeconds(1));

        for (int i = 0; i < 50; i++)
        {
            RateLimitAcquireResult result = await limiter.TryAcquireAsync(scope, CancellationToken.None);
            result.Acquired.Should().BeTrue($"acquisition {i + 1} after the refill should succeed again");
        }
    }

    [Fact]
    public async Task ReportRateLimitedAsync_WithRetryAfter_BlocksAcquisitionsForThatManySeconds()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);
        RateLimitScope scope = Scope("key-a");

        DateTimeOffset penaltyUntil = await limiter.ReportRateLimitedAsync(
            scope, new RateLimitSignal { RetrySeconds = 3, StatusCode = 429 }, CancellationToken.None);

        penaltyUntil.Should().Be(timeProvider.GetUtcNow().AddSeconds(3));

        timeProvider.Advance(TimeSpan.FromSeconds(2));
        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeFalse(
            "the 3 second penalty has not elapsed yet");

        timeProvider.Advance(TimeSpan.FromSeconds(1).Add(TimeSpan.FromMilliseconds(1)));
        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeTrue(
            "the penalty has elapsed and tokens are available");
    }

    [Fact]
    public async Task ReportSuccessAsync_RatelimitRemainingZero_DefersTheNextAcquisitionToTheNextSecondBoundary()
    {
        DateTimeOffset start = new DateTimeOffset(2024, 1, 1, 10, 0, 0, 250, TimeSpan.Zero);
        FakeTimeProvider timeProvider = new FakeTimeProvider(start);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);
        RateLimitScope scope = Scope("key-a");

        await limiter.ReportSuccessAsync(
            scope, new RateLimitSignal { Remaining = 0, StatusCode = 200 }, CancellationToken.None);

        RateLimitAcquireResult result = await limiter.TryAcquireAsync(scope, CancellationToken.None);

        result.Acquired.Should().BeFalse();
        result.NextRequestAllowedAt.Should().Be(new DateTimeOffset(2024, 1, 1, 10, 0, 1, 0, TimeSpan.Zero));

        timeProvider.SetUtcNow(new DateTimeOffset(2024, 1, 1, 10, 0, 1, 0, TimeSpan.Zero));
        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeTrue();
    }

    [Fact]
    public async Task TryAcquireAsync_TwoDifferentApiKeys_HaveIndependentBuckets()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);

        for (int i = 0; i < 50; i++)
        {
            await limiter.TryAcquireAsync(Scope("key-a"), CancellationToken.None);
        }

        (await limiter.TryAcquireAsync(Scope("key-a"), CancellationToken.None)).Acquired.Should().BeFalse();
        (await limiter.TryAcquireAsync(Scope("key-b"), CancellationToken.None)).Acquired.Should().BeTrue(
            "key-b's bucket must not be affected by key-a exhausting its own bucket");
    }

    [Fact]
    public async Task TryAcquireAsync_OneHundredParallelCallers_NeverAcquireMoreThanFiftyInOneSecond()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);
        RateLimitScope scope = Scope("key-a");

        IEnumerable<Task<RateLimitAcquireResult>> tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => limiter.TryAcquireAsync(scope, CancellationToken.None)));

        RateLimitAcquireResult[] results = await Task.WhenAll(tasks);

        results.Count(r => r.Acquired).Should().Be(50);
    }

    // ── Minimum request interval ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_WithMinimumInterval_RefusesTheSecondRequestUntilTheIntervalElapses()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider, TimeSpan.FromSeconds(15));
        RateLimitScope scope = Scope("key-a");

        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeTrue();

        RateLimitAcquireResult tooSoon = await limiter.TryAcquireAsync(scope, CancellationToken.None);

        tooSoon.Acquired.Should().BeFalse("the minimum interval has not elapsed since the last request");
        tooSoon.NextRequestAllowedAt.Should().Be(timeProvider.GetUtcNow().AddSeconds(15));

        timeProvider.Advance(TimeSpan.FromSeconds(14));
        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeFalse(
            "14 seconds is still short of the 15 second interval");

        timeProvider.Advance(TimeSpan.FromSeconds(1));
        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeTrue(
            "a request exactly one interval after the previous one is due");
    }

    [Fact]
    public async Task TryAcquireAsync_ZeroMinimumInterval_AllowsBackToBackRequests()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);
        RateLimitScope scope = Scope("key-a");

        for (int i = 0; i < 10; i++)
        {
            (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeTrue(
                $"request {i + 1} must not be paced by a disabled interval");
        }
    }

    [Fact]
    public async Task TryAcquireAsync_MinimumInterval_IsSharedByEveryMethodOfOneApiKey()
    {
        // The same connection drives FBO and FBS against one Client-Id, so the interval has to span
        // both methods: pacing them independently would let the two runs reach Ozon back to back.
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider, TimeSpan.FromSeconds(15));
        RateLimitScope fboScope = new RateLimitScope("ozon", "key-a", OrdersRateLimiterMethods.OzonPostingFboList);
        RateLimitScope fbsScope = new RateLimitScope("ozon", "key-a", OrdersRateLimiterMethods.OzonPostingFbsList);

        (await limiter.TryAcquireAsync(fboScope, CancellationToken.None)).Acquired.Should().BeTrue();

        RateLimitAcquireResult fbsResult = await limiter.TryAcquireAsync(fbsScope, CancellationToken.None);

        fbsResult.Acquired.Should().BeFalse();
        fbsResult.NextRequestAllowedAt.Should().Be(timeProvider.GetUtcNow().AddSeconds(15));

        (await limiter.TryAcquireAsync(Scope("key-b"), CancellationToken.None)).Acquired.Should().BeTrue(
            "another API key is paced by its own interval");
    }

    [Fact]
    public async Task GetNextRequestAllowedAtAsync_WithMinimumInterval_ReportsTheIntervalWithoutReservingASlot()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider, TimeSpan.FromSeconds(15));
        RateLimitScope scope = Scope("key-a");

        (await limiter.GetNextRequestAllowedAtAsync(scope, CancellationToken.None)).Should().BeNull(
            "a bucket no request has touched yet has nothing to pace against");

        await limiter.TryAcquireAsync(scope, CancellationToken.None);

        (await limiter.GetNextRequestAllowedAtAsync(scope, CancellationToken.None))
            .Should().Be(timeProvider.GetUtcNow().AddSeconds(15));

        // Reading the instant must not consume the interval: the scheduler asks on every tick.
        (await limiter.TryAcquireAsync(scope, CancellationToken.None)).Acquired.Should().BeFalse();

        timeProvider.Advance(TimeSpan.FromSeconds(15));

        (await limiter.GetNextRequestAllowedAtAsync(scope, CancellationToken.None)).Should().BeNull();
    }

    // ── Bucket dictionary bound ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_ManyKeys_KeepsTheBucketDictionaryProportionalToActiveKeys()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider, TimeSpan.FromMinutes(1));

        for (int i = 0; i < 100; i++)
        {
            await limiter.TryAcquireAsync(Scope($"client-{i}"), CancellationToken.None);
        }

        limiter.BucketCount.Should().Be(100);

        // Four minimum intervals pass, so every bucket touched during the burst is idle. The next
        // insert evicts them before adding its own.
        timeProvider.Advance(TimeSpan.FromMinutes(5));

        await limiter.TryAcquireAsync(Scope("client-fresh"), CancellationToken.None);

        limiter.BucketCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireAsync_WithNoMinimumInterval_EvictsBucketsIdleForTenMinutes()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);

        await limiter.TryAcquireAsync(Scope("client-old"), CancellationToken.None);

        timeProvider.Advance(TimeSpan.FromMinutes(11));

        await limiter.TryAcquireAsync(Scope("client-new"), CancellationToken.None);

        limiter.BucketCount.Should().Be(1);
    }

    [Fact]
    public async Task TryAcquireAsync_SameRawKeyUnderTwoMethods_SharesOneBucket()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = CreateLimiter(timeProvider);

        await limiter.TryAcquireAsync(Scope("client-id"), CancellationToken.None);
        await limiter.TryAcquireAsync(
            new RateLimitScope("ozon", "client-id", OrdersRateLimiterMethods.OzonPostingFbsList),
            CancellationToken.None);

        limiter.BucketCount.Should().Be(1);
    }
}
