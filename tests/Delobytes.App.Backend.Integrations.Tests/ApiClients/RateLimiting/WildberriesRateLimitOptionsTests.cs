using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.RateLimiting;

/// <summary>
/// Tests for <see cref="WildberriesRateLimitOptions.ResolvePolicy"/>: per-method overrides layered
/// over the root-level defaults, so an existing configuration keeps its meaning when the
/// <c>Methods</c> section is absent.
/// </summary>
public class WildberriesRateLimitOptionsTests
{
    [Fact]
    public void ResolvePolicy_NoMethodSection_ReturnsEveryRootValue()
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

        policy.InitialIntervalSeconds.Should().Be(60);
        policy.MinIntervalSeconds.Should().Be(5);
        policy.FallbackIntervalSeconds.Should().Be(3600);
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
                    MinIntervalSeconds = 1,
                },
            },
        };

        WildberriesRateLimitOptions.ResolvedRateLimitPolicy policy =
            options.ResolvePolicy(WildberriesApiMethodRegistry.ContentCardsList);

        policy.InitialIntervalSeconds.Should().Be(10);
        policy.MinIntervalSeconds.Should().Be(1);
        policy.FallbackIntervalSeconds.Should().Be(3600, "an unset override inherits the root value");
        policy.BackoffPadSeconds.Should().Be(7);
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
