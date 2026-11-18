using Delobytes.App.Backend.Integrations.Application.Models;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.RateLimiting;

/// <summary>
/// Tests for <see cref="WildberriesApiMethodRegistry"/>: the catalogue the rate limiter validates
/// every scope against.
/// </summary>
public class WildberriesApiMethodRegistryTests
{
    [Fact]
    public void Registry_ContainsExpectedMethods()
    {
        WildberriesApiMethodRegistry.All.Should().Contain(new[]
        {
            WildberriesApiMethodRegistry.SupplierOrders,
            WildberriesApiMethodRegistry.ContentCardsList,
            WildberriesApiMethodRegistry.SellerInfo,
            WildberriesApiMethodRegistry.FinanceSalesReportsDetailed
        });

        WildberriesApiMethodRegistry.All.Should().HaveCount(4);
    }

    [Fact]
    public void IsKnown_ReturnsTrueForRegisteredMethods()
    {
        WildberriesApiMethodRegistry.IsKnown(WildberriesApiMethodRegistry.SupplierOrders).Should().BeTrue();
        WildberriesApiMethodRegistry.IsKnown(WildberriesApiMethodRegistry.ContentCardsList).Should().BeTrue();
        WildberriesApiMethodRegistry.IsKnown(WildberriesApiMethodRegistry.SellerInfo).Should().BeTrue();
        WildberriesApiMethodRegistry.IsKnown(WildberriesApiMethodRegistry.FinanceSalesReportsDetailed).Should().BeTrue();
    }

    [Fact]
    public void IsKnown_ReturnsFalseForUnknownMethod()
    {
        WildberriesApiMethodRegistry.IsKnown("unknown/method").Should().BeFalse();
    }
}

