using System;
using System.Net;
using System.Net.Http;
using System.Text;

using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients;

/// <summary>
/// Tests for <see cref="WildberriesRateLimitHeaders"/> against the two real Wildberries captures
/// quoted in the backfill pacing specification (a throttled 429 and a successful 200), verbatim.
/// </summary>
public class WildberriesRateLimitHeadersTests
{
    [Fact]
    public void Parse_RealThrottledCapture_YieldsExactlyTheDocumentedFields()
    {
        using HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
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
        response.Headers.TryAddWithoutValidation("x-proxied", "false");

        RateLimitSignal signal = WildberriesRateLimitHeaders.Parse(response);

        signal.StatusCode.Should().Be(429);
        signal.Limit.Should().Be(10);
        signal.Remaining.Should().Be(0);
        signal.ResetSeconds.Should().Be(579);
        signal.RetrySeconds.Should().Be(39);
        signal.RequestId.Should().Be("fcdda24eaf40d3b4a1273b3065b71c5c");
    }

    [Fact]
    public void Parse_RealSuccessCapture_LeavesResetAndRetryNullWithoutError()
    {
        using HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json"),
        };

        response.Headers.TryAddWithoutValidation("x-ratelimit-limit", "10");
        response.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "9");
        response.Headers.TryAddWithoutValidation("x-request-id", "4b576135a03dfb1d278930bc40fcd0e7");
        response.Headers.TryAddWithoutValidation("x-proxied", "true");

        RateLimitSignal signal = WildberriesRateLimitHeaders.Parse(response);

        signal.StatusCode.Should().Be(200);
        signal.Limit.Should().Be(10);
        signal.Remaining.Should().Be(9);
        signal.RequestId.Should().Be("4b576135a03dfb1d278930bc40fcd0e7");

        // A 200 never carries reset/retry (see spec sect. 1.2, fact 4): this is the ordinary case
        // on a success, not a parsing failure, and must not raise a warning or throw.
        signal.ResetSeconds.Should().BeNull();
        signal.RetrySeconds.Should().BeNull();
    }

    [Fact]
    public void Parse_XProxiedHeader_IsIgnoredRegardlessOfValue()
    {
        // x-proxied differs between the two real captures (true on success, false on 429); nothing
        // in RateLimitSignal reads it, and parsing must not branch on it or fail because of it.
        using HttpResponseMessage okResponse = new HttpResponseMessage(HttpStatusCode.OK);
        okResponse.Headers.TryAddWithoutValidation("x-ratelimit-limit", "10");
        okResponse.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "9");
        okResponse.Headers.TryAddWithoutValidation("x-proxied", "true");

        using HttpResponseMessage throttledResponse = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        throttledResponse.Headers.TryAddWithoutValidation("x-ratelimit-limit", "10");
        throttledResponse.Headers.TryAddWithoutValidation("x-ratelimit-remaining", "0");
        throttledResponse.Headers.TryAddWithoutValidation("x-proxied", "false");

        RateLimitSignal okSignal = WildberriesRateLimitHeaders.Parse(okResponse);
        RateLimitSignal throttledSignal = WildberriesRateLimitHeaders.Parse(throttledResponse);

        okSignal.Limit.Should().Be(10);
        throttledSignal.Limit.Should().Be(10);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-number")]
    [InlineData("-5")]
    public void Parse_MalformedOrNegativeNumericHeader_YieldsNullInsteadOfThrowing(string rawValue)
    {
        using HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        response.Headers.TryAddWithoutValidation("x-ratelimit-retry", rawValue);

        Action act = () => WildberriesRateLimitHeaders.Parse(response);

        act.Should().NotThrow();
        WildberriesRateLimitHeaders.Parse(response).RetrySeconds.Should().BeNull();
    }

    [Fact]
    public void Parse_NoHeadersAtAll_ReturnsAllNullsRatherThanThrowing()
    {
        using HttpResponseMessage response = new HttpResponseMessage(HttpStatusCode.OK);

        RateLimitSignal signal = WildberriesRateLimitHeaders.Parse(response);

        signal.Limit.Should().BeNull();
        signal.Remaining.Should().BeNull();
        signal.ResetSeconds.Should().BeNull();
        signal.RetrySeconds.Should().BeNull();
        signal.RequestId.Should().BeNull();
        signal.StatusCode.Should().Be(200);
    }
}
