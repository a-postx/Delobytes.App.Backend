using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.Services;

/// <summary>
/// Tests for <see cref="WildberriesRateLimiter"/>: the pacing algorithm behind
/// <c>TryAcquireAsync</c>, <c>ReportRateLimitedAsync</c> and <c>ReportSuccessAsync</c>, specified in
/// Stage 2 of the backfill pacing work and exercised against the real captures quoted there.
/// </summary>
public class WildberriesRateLimiterTests
{
    private const string ApiKey = "wb-secret-key-0123456789";

    private static WildberriesRateLimiter CreateLimiter(
        FakeWildberriesMethodThrottleRepository repository,
        WildberriesRateLimitOptions? options = null,
        ILogger<WildberriesRateLimiter>? logger = null)
    {
        return new WildberriesRateLimiter(
            repository,
            Options.Create(options ?? new WildberriesRateLimitOptions()),
            logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WildberriesRateLimiter>.Instance);
    }

    private static RateLimitSignal BuildThrottledSignal(
        int? limit = 10, int? remaining = 0, int? resetSeconds = 579, int? retrySeconds = 39, string requestId = "req-429")
    {
        return new RateLimitSignal
        {
            Limit = limit,
            Remaining = remaining,
            ResetSeconds = resetSeconds,
            RetrySeconds = retrySeconds,
            RequestId = requestId,
            StatusCode = 429,
        };
    }

    private static RateLimitSignal BuildSuccessSignal(int? limit = 10, int? remaining = 9, string requestId = "req-200")
    {
        return new RateLimitSignal
        {
            Limit = limit,
            Remaining = remaining,
            ResetSeconds = null,
            RetrySeconds = null,
            RequestId = requestId,
            StatusCode = 200,
        };
    }

    private static List<string> CaptureLogMessages(Mock<ILogger<WildberriesRateLimiter>> loggerMock)
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
    public async Task TryAcquireAsync_SecondCallBeforeIntervalElapses_ReturnsFalseAndLeavesStateUnchanged()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        RateLimitAcquireResult first = await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        WildberriesMethodThrottle? afterFirst = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        RateLimitAcquireResult second = await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        WildberriesMethodThrottle? afterSecond = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        first.Acquired.Should().BeTrue();
        second.Acquired.Should().BeFalse();
        second.NextRequestAllowedAt.Should().Be(afterFirst!.NextRequestAllowedAt, "a refusal reports the stored slot the caller must wait for");

        afterSecond.Should().NotBeNull();
        afterSecond!.NextRequestAllowedAt.Should().Be(afterFirst.NextRequestAllowedAt);
        afterSecond.LastRequestAt.Should().Be(afterFirst.LastRequestAt);
    }

    [Fact]
    public async Task TryAcquireAsync_DifferentMethodSameKey_IsNotBlockedByTheOtherMethodsInterval()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });

        bool ordersAcquired = (await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None)).Acquired;
        bool cardsAcquired = (await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.ContentCardsList, CancellationToken.None)).Acquired;

        ordersAcquired.Should().BeTrue();
        cardsAcquired.Should().BeTrue("the limit applies per path, not per base URL or per key alone");
    }

    [Fact]
    public async Task TryAcquireAsync_SameKeyUsedByTwoConnections_ShareOneAllowance()
    {
        // The limiter is keyed by the API key fingerprint only - there is no connection id
        // anywhere in IWildberriesRateLimiter - so two Connections configured with the same raw
        // Wildberries key necessarily observe the same pacing row by construction.
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });

        bool connectionAAcquired = (await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None)).Acquired;
        bool connectionBAcquired = (await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None)).Acquired;

        connectionAAcquired.Should().BeTrue();
        connectionBAcquired.Should().BeFalse();
    }

    [Fact]
    public async Task ReportSuccessAsync_NoObservedWindow_DoesNotDeriveIntervalAndStepsDownOnlyViaSuccessStreak()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            MinIntervalSeconds = 5,
            SpeedUpAfterSuccesses = 3,
        };
        WildberriesRateLimiter limiter = CreateLimiter(repository, options);
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        RateLimitSignal signal = BuildSuccessSignal(limit: 10, remaining: 9);

        Func<Task> act = async () =>
        {
            await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, signal, CancellationToken.None);
            await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, signal, CancellationToken.None);
            await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, signal, CancellationToken.None);
        };

        await act.Should().NotThrowAsync();

        WildberriesMethodThrottle? row = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        row.Should().NotBeNull();
        row!.ObservedWindowSeconds.Should().BeNull("a 200 never carries reset, so the window length must still be unknown");
        row.CurrentIntervalSeconds.Should().Be((int)(60 * 0.8), "three successes with no observed window step the pace down by the SuccessStreak rule alone");
        row.SuccessStreak.Should().Be(0);
    }

    [Fact]
    public async Task ReportSuccessAsync_AfterObservedWindowFromA429_ConvergesIntervalWithoutAnotherThrottle()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            MinIntervalSeconds = 5,
            BackoffPadSeconds = 5,
            FallbackIntervalSeconds = 3600,
        };
        WildberriesRateLimiter limiter = CreateLimiter(repository, options);
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        await limiter.ReportRateLimitedAsync(
            ApiKey,
            WildberriesApiMethod.SupplierOrders,
            BuildThrottledSignal(limit: 10, remaining: 0, resetSeconds: 579, retrySeconds: 39),
            CancellationToken.None);

        await limiter.ReportSuccessAsync(
            ApiKey, WildberriesApiMethod.SupplierOrders, BuildSuccessSignal(limit: 10, remaining: 9), CancellationToken.None);

        WildberriesMethodThrottle? row = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        row.Should().NotBeNull();
        row!.ObservedWindowSeconds.Should().Be(579);
        row.CurrentIntervalSeconds.Should().Be(57, "579s window / limit 10 derives a 57s sustainable pace");
    }

    [Fact]
    public async Task ReportSuccessAsync_RemainingZero_LeavesNextRequestAllowedAtUntouched()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        WildberriesMethodThrottle? beforeReport = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        DateTimeOffset nextRequestAllowedAtBefore = beforeReport!.NextRequestAllowedAt;

        await limiter.ReportSuccessAsync(
            ApiKey, WildberriesApiMethod.SupplierOrders, BuildSuccessSignal(limit: 10, remaining: 0), CancellationToken.None);

        WildberriesMethodThrottle? afterReport = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        afterReport.Should().NotBeNull();
        afterReport!.NextRequestAllowedAt.Should().Be(nextRequestAllowedAtBefore, "the window length is unknown when remaining=0, so no wait can be computed");
    }

    [Fact]
    public async Task ReportRateLimitedAsync_HonoursRetryNotReset_SetsNextAttemptAtRetryPlusPad()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions { InitialIntervalSeconds = 60, BackoffPadSeconds = 5 };
        WildberriesRateLimiter limiter = CreateLimiter(repository, options);
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        DateTimeOffset before = DateTimeOffset.UtcNow;

        await limiter.ReportRateLimitedAsync(
            ApiKey,
            WildberriesApiMethod.SupplierOrders,
            BuildThrottledSignal(limit: 10, remaining: 0, resetSeconds: 579, retrySeconds: 39),
            CancellationToken.None);

        DateTimeOffset after = DateTimeOffset.UtcNow;

        WildberriesMethodThrottle? row = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        row.Should().NotBeNull();

        // Real capture: retry=39s is the server's advice for the next call; reset=579s describes
        // the whole window and must not be used as the wait.
        row!.NextRequestAllowedAt.Should().BeOnOrAfter(before.AddSeconds(39 + 5));
        row.NextRequestAllowedAt.Should().BeOnOrBefore(after.AddSeconds(39 + 5 + 2));
        row.ObservedWindowSeconds.Should().Be(579);
    }

    [Fact]
    public async Task ReportRateLimitedAsync_MidSuccessStreak_ResetsStreakAndDelaysTheNextSpeedUp()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            MinIntervalSeconds = 5,
            SpeedUpAfterSuccesses = 3,
            BackoffPadSeconds = 5,
        };
        WildberriesRateLimiter limiter = CreateLimiter(repository, options);
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        RateLimitSignal noWindowSuccess = BuildSuccessSignal(limit: null, remaining: null);

        await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, noWindowSuccess, CancellationToken.None);
        await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, noWindowSuccess, CancellationToken.None);

        WildberriesMethodThrottle? afterTwoSuccessesRow = await repository
            .GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        afterTwoSuccessesRow!.CurrentIntervalSeconds.Should().Be(60, "two successes are one short of the three required to speed up");
        afterTwoSuccessesRow!.SuccessStreak.Should().Be(2);

        await limiter.ReportRateLimitedAsync(
            ApiKey,
            WildberriesApiMethod.SupplierOrders,
            BuildThrottledSignal(limit: null, remaining: null, resetSeconds: null, retrySeconds: 10),
            CancellationToken.None);

        WildberriesMethodThrottle? afterThrottleRow = await repository
            .GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        int afterThrottleInterval = afterThrottleRow!.CurrentIntervalSeconds;

        afterThrottleRow.SuccessStreak.Should().Be(0, "a 429 in the middle of a success streak must reset it");

        await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, noWindowSuccess, CancellationToken.None);
        await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, noWindowSuccess, CancellationToken.None);

        WildberriesMethodThrottle? afterTwoMoreSuccessesRow = await repository
            .GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        afterTwoMoreSuccessesRow!.CurrentIntervalSeconds.Should().Be(
            afterThrottleInterval,
            "the streak needed three fresh successes after the 429; two is still not enough to speed up again");
        afterTwoMoreSuccessesRow.SuccessStreak.Should().Be(2);

        await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, noWindowSuccess, CancellationToken.None);

        WildberriesMethodThrottle? afterThirdSuccessRow = await repository
            .GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        afterThirdSuccessRow!.CurrentIntervalSeconds.Should().Be((int)(afterThrottleInterval * 0.8));
        afterThirdSuccessRow.SuccessStreak.Should().Be(0);
    }

    [Fact]
    public async Task NoLogStatement_EverContainsTheRawApiKey()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        Mock<ILogger<WildberriesRateLimiter>> loggerMock = new();
        List<string> loggedMessages = CaptureLogMessages(loggerMock);

        WildberriesRateLimiter limiter = CreateLimiter(
            repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 }, loggerMock.Object);

        await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        await limiter.ReportRateLimitedAsync(ApiKey, WildberriesApiMethod.SupplierOrders, BuildThrottledSignal(), CancellationToken.None);
        await limiter.ReportSuccessAsync(ApiKey, WildberriesApiMethod.SupplierOrders, BuildSuccessSignal(), CancellationToken.None);

        loggedMessages.Should().NotBeEmpty();
        loggedMessages.Should().OnlyContain(message => !message.Contains(ApiKey), "pacing state and logs must address a key only through its fingerprint");
    }

    [Fact]
    public async Task AdaptiveBehaviour_SlowsOnThrottleThenConvergesThenStepsDownWithoutHeaders()
    {
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            MinIntervalSeconds = 5,
            FallbackIntervalSeconds = 3600,
            BackoffPadSeconds = 5,
            SpeedUpAfterSuccesses = 3,
        };

        // --- a 429 carrying the real capture's headers slows the limiter to ~44s, not ~579s ---
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository, options);
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        DateTimeOffset beforeThrottle = DateTimeOffset.UtcNow;

        await limiter.ReportRateLimitedAsync(
            ApiKey,
            WildberriesApiMethod.SupplierOrders,
            BuildThrottledSignal(limit: 10, remaining: 0, resetSeconds: 579, retrySeconds: 39),
            CancellationToken.None);

        WildberriesMethodThrottle? afterThrottle = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        TimeSpan waitUntilNextAttempt = afterThrottle!.NextRequestAllowedAt - beforeThrottle;
        waitUntilNextAttempt.TotalSeconds.Should().BeApproximately(44, 2);

        // --- a subsequent 200 carrying only limit/remaining converges the pace to ~57s ---
        await limiter.ReportSuccessAsync(
            ApiKey, WildberriesApiMethod.SupplierOrders, BuildSuccessSignal(limit: 10, remaining: 9), CancellationToken.None);

        WildberriesMethodThrottle? afterConverge = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        afterConverge!.CurrentIntervalSeconds.Should().Be(57);

        // --- a different key/method pair with no usable headers steps down toward
        //     MinIntervalSeconds via SuccessStreak alone ---
        FakeWildberriesMethodThrottleRepository repositoryWithoutHeaders = new();
        WildberriesRateLimiter limiterWithoutHeaders = CreateLimiter(repositoryWithoutHeaders, options);
        const string otherKey = "wb-secret-key-no-headers";
        string otherFingerprint = WildberriesKeyFingerprint.Compute(otherKey);

        RateLimitSignal noHeaders = BuildSuccessSignal(limit: null, remaining: null);

        for (int i = 0; i < 3; i++)
        {
            await limiterWithoutHeaders.ReportSuccessAsync(otherKey, WildberriesApiMethod.SupplierOrders, noHeaders, CancellationToken.None);
        }

        WildberriesMethodThrottle? steppedDown = await repositoryWithoutHeaders.GetAsync(
            otherFingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        steppedDown!.CurrentIntervalSeconds.Should().Be((int)(options.InitialIntervalSeconds * 0.8));
        steppedDown.CurrentIntervalSeconds.Should().BeLessThan(options.InitialIntervalSeconds);
    }

    [Fact]
    public async Task ReportRateLimitedAsync_RetryFarBeyondFallback_SetsCurrentIntervalToRetryWithoutFallbackCap()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            FallbackIntervalSeconds = 3600,
            BackoffPadSeconds = 5,
        };
        WildberriesRateLimiter limiter = CreateLimiter(repository, options);
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        await limiter.ReportRateLimitedAsync(
            ApiKey,
            WildberriesApiMethod.SupplierOrders,
            BuildThrottledSignal(limit: null, remaining: null, resetSeconds: null, retrySeconds: 10800),
            CancellationToken.None);

        WildberriesMethodThrottle? row = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        row.Should().NotBeNull();
        row!.CurrentIntervalSeconds.Should().Be(
            10800, "a server-advised wait is ground truth and must not be clamped by FallbackIntervalSeconds");
    }

    [Fact]
    public async Task ReportRateLimitedAsync_NoRetryOrResetHeaders_CurrentIntervalIsCappedByFallback()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            FallbackIntervalSeconds = 3600,
            BackoffPadSeconds = 5,
        };
        WildberriesRateLimiter limiter = CreateLimiter(repository, options);
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        // No RetrySeconds/ResetSeconds: the wait falls back to the current interval, and the
        // resulting CurrentIntervalSeconds must still respect the fallback cap.
        await limiter.ReportRateLimitedAsync(
            ApiKey,
            WildberriesApiMethod.SupplierOrders,
            BuildThrottledSignal(limit: null, remaining: null, resetSeconds: null, retrySeconds: null),
            CancellationToken.None);

        WildberriesMethodThrottle? row = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        row.Should().NotBeNull();
        row!.CurrentIntervalSeconds.Should().BeLessOrEqualTo(options.FallbackIntervalSeconds);
    }

    [Fact]
    public async Task GetNextRequestAllowedAtAsync_DoesNotMutateState()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository, new WildberriesRateLimitOptions { InitialIntervalSeconds = 60 });
        string fingerprint = WildberriesKeyFingerprint.Compute(ApiKey);

        await limiter.TryAcquireAsync(ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        WildberriesMethodThrottle? before = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        DateTimeOffset nextAllowed = await limiter.GetNextRequestAllowedAtAsync(
            ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        WildberriesMethodThrottle? after = await repository.GetAsync(fingerprint, WildberriesApiMethod.SupplierOrders, CancellationToken.None);

        nextAllowed.Should().Be(before!.NextRequestAllowedAt);
        after!.NextRequestAllowedAt.Should().Be(before.NextRequestAllowedAt);
        after.LastRequestAt.Should().Be(before.LastRequestAt);
    }

    [Fact]
    public async Task GetNextRequestAllowedAtAsync_NoRowExists_ReturnsNow()
    {
        FakeWildberriesMethodThrottleRepository repository = new();
        WildberriesRateLimiter limiter = CreateLimiter(repository, new WildberriesRateLimitOptions());

        DateTimeOffset before = DateTimeOffset.UtcNow;
        DateTimeOffset nextAllowed = await limiter.GetNextRequestAllowedAtAsync(
            ApiKey, WildberriesApiMethod.SupplierOrders, CancellationToken.None);
        DateTimeOffset after = DateTimeOffset.UtcNow;

        nextAllowed.Should().BeOnOrAfter(before);
        nextAllowed.Should().BeOnOrBefore(after);
    }
}
