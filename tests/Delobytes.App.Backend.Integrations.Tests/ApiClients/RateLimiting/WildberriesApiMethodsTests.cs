using System;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.RateLimiting;

public class WildberriesApiMethodsTests
{
    [Fact]
    public void FromRateLimitMethod_SupplierOrders_MapsToTheOrdersMethod()
    {
        WildberriesApiMethod method = WildberriesApiMethods.FromRateLimitMethod(
            OrdersRateLimiterMethods.WildberriesSupplierOrders);

        method.Should().Be(WildberriesApiMethod.SupplierOrders);
    }

    [Fact]
    public void FromRateLimitMethod_UnknownName_ThrowsInsteadOfPacingTheWrongBucket()
    {
        Action act = () => WildberriesApiMethods.FromRateLimitMethod("content/cards/list");

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
