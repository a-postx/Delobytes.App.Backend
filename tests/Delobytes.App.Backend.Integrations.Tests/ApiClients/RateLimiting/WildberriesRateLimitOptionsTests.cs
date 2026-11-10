using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.RateLimiting;

/// <summary>
/// Tests for <see cref="WildberriesRateLimitOptions.ResolvePolicy"/>: per-field precedence of the
/// configuration's method override, the method's built-in default and the root-level value.
/// </summary>
public class WildberriesRateLimitOptionsTests
{
    [Fact]
    public void ResolvePolicy_MethodWithoutBuiltInDefaults_ReturnsEveryRootValue()
    {
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            MinIntervalSeconds = 5,
            FallbackIntervalSeconds = 3600,
            BackoffPadSeconds = 7,
            SpeedUpAfterSuccesses = 4,
        };

        WildberriesRateLimitOptions.ResolvedRateLimitPolicy policy =
            options.ResolvePolicy(WildberriesApiMethodRegistry.SellerInfo);

        policy.InitialIntervalSeconds.Should().Be(60);
        policy.MinIntervalSeconds.Should().Be(5);
        policy.FallbackIntervalSeconds.Should().Be(3600);
        policy.BackoffPadSeconds.Should().Be(7);
        policy.SpeedUpAfterSuccesses.Should().Be(4);
    }

    [Fact]
    public void ResolvePolicy_ContentCards_UsesBuiltInDefaultsForPaceAndRootsForTheRest()
    {
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            MinIntervalSeconds = 5,
            FallbackIntervalSeconds = 3600,
            BackoffPadSeconds = 7,
            SpeedUpAfterSuccesses = 4,
        };

        WildberriesRateLimitOptions.ResolvedRateLimitPolicy policy =
            options.ResolvePolicy(WildberriesApiMethodRegistry.ContentCardsList);

        // The orders root values (60s start, 5s floor, 1h no-header fallback) made every 50-card
        // page cost a minute; the cards endpoint has a far larger allowance.
        policy.InitialIntervalSeconds.Should().Be(1);
        policy.MinIntervalSeconds.Should().Be(1);
        policy.FallbackIntervalSeconds.Should().Be(60);

        // Everything without a method-level opinion still comes from the root configuration.
        policy.BackoffPadSeconds.Should().Be(7);
        policy.SpeedUpAfterSuccesses.Should().Be(4);
    }

    [Fact]
    public void ResolvePolicy_MethodEntry_OverridesOnlyTheValuesItSets()
    {
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            MinIntervalSeconds = 5,
            FallbackIntervalSeconds = 3600,
            BackoffPadSeconds = 7,
            SpeedUpAfterSuccesses = 4,
            Methods =
            {
                [WildberriesApiMethodRegistry.ContentCardsList] = new WildberriesRateLimitOptions.MethodRateLimitPolicy
                {
                    InitialIntervalSeconds = 10,
                },
            },
        };

        WildberriesRateLimitOptions.ResolvedRateLimitPolicy policy =
            options.ResolvePolicy(WildberriesApiMethodRegistry.ContentCardsList);

        policy.InitialIntervalSeconds.Should().Be(10, "the configuration override outranks the built-in default");
        policy.MinIntervalSeconds.Should().Be(1, "an unset field keeps the method's built-in default");
        policy.FallbackIntervalSeconds.Should().Be(60);
        policy.BackoffPadSeconds.Should().Be(7, "a method without an opinion on this field inherits the root value");
        policy.SpeedUpAfterSuccesses.Should().Be(4);
    }

    [Fact]
    public void ResolvePolicy_ConfiguredMethod_DoesNotAffectSiblingMethods()
    {
        WildberriesRateLimitOptions options = new WildberriesRateLimitOptions
        {
            InitialIntervalSeconds = 60,
            Methods =
            {
                [WildberriesApiMethodRegistry.ContentCardsList] = new WildberriesRateLimitOptions.MethodRateLimitPolicy
                {
                    InitialIntervalSeconds = 10,
                },
            },
        };

        WildberriesRateLimitOptions.ResolvedRateLimitPolicy ordersPolicy =
            options.ResolvePolicy(WildberriesApiMethodRegistry.SupplierOrders);

        ordersPolicy.InitialIntervalSeconds.Should().Be(60);
    }
}
