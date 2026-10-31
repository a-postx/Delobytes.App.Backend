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

    [Fact]
    public async Task TryAcquireAsync_FiftyAcquisitionsInOneSecond_SucceedAndTheFiftyFirstIsRefusedWithAFutureInstant()
    {
        FakeTimeProvider timeProvider = new FakeTimeProvider(DateTimeOffset.UtcNow);
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider);
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
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider);
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
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider);
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
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider);
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
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider);

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
        OzonRateLimiter limiter = new OzonRateLimiter(timeProvider);
        RateLimitScope scope = Scope("key-a");

        IEnumerable<Task<RateLimitAcquireResult>> tasks = Enumerable.Range(0, 100)
            .Select(_ => Task.Run(() => limiter.TryAcquireAsync(scope, CancellationToken.None)));

        RateLimitAcquireResult[] results = await Task.WhenAll(tasks);

        results.Count(r => r.Acquired).Should().Be(50);
    }
}
