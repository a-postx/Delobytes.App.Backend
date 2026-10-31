using System;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;
using FluentAssertions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Integrations.Tests.ApiClients.RateLimiting;

/// <summary>
/// Tests for <see cref="OrdersRateLimiterRouter"/>: dispatch by <see cref="RateLimitScope.ChannelCode"/>.
/// </summary>
public class OrdersRateLimiterRouterTests
{
    private static OrdersRateLimiterRouter CreateRouter(out IWildberriesRateLimiter wildberriesRateLimiter)
    {
        Mock<IWildberriesRateLimiter> wildberriesMock = new Mock<IWildberriesRateLimiter>();
        wildberriesMock
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<WildberriesApiMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RateLimitAcquireResult { Acquired = true, NextRequestAllowedAt = DateTimeOffset.UtcNow });

        wildberriesRateLimiter = wildberriesMock.Object;

        WildberriesOrdersRateLimiterAdapter wildberriesAdapter =
            new WildberriesOrdersRateLimiterAdapter(wildberriesRateLimiter);
        OzonRateLimiter ozonRateLimiter = new OzonRateLimiter(new FakeTimeProvider(DateTimeOffset.UtcNow));

        return new OrdersRateLimiterRouter(wildberriesAdapter, ozonRateLimiter);
    }

    [Fact]
    public async Task TryAcquireAsync_WildberriesChannel_RoutesToTheWildberriesAdapter()
    {
        OrdersRateLimiterRouter router = CreateRouter(out IWildberriesRateLimiter wildberriesRateLimiter);
        RateLimitScope scope = new RateLimitScope(
            "wildberries", "wb-key", OrdersRateLimiterMethods.WildberriesSupplierOrders);

        RateLimitAcquireResult result = await router.TryAcquireAsync(scope, CancellationToken.None);

        result.Acquired.Should().BeTrue();
        Mock.Get(wildberriesRateLimiter).Verify(
            x => x.TryAcquireAsync("wb-key", WildberriesApiMethod.SupplierOrders, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task TryAcquireAsync_OzonChannel_RoutesToTheOzonLimiter()
    {
        OrdersRateLimiterRouter router = CreateRouter(out _);
        RateLimitScope scope = new RateLimitScope("ozon", "ozon-key", OrdersRateLimiterMethods.OzonPostingFboList);

        RateLimitAcquireResult result = await router.TryAcquireAsync(scope, CancellationToken.None);

        result.Acquired.Should().BeTrue("the Ozon bucket starts full");
    }

    [Fact]
    public async Task TryAcquireAsync_UnknownChannel_ThrowsNotSupportedException()
    {
        OrdersRateLimiterRouter router = CreateRouter(out _);
        RateLimitScope scope = new RateLimitScope("yandex.kit", "any-key", "any-method");

        Func<Task> act = async () => await router.TryAcquireAsync(scope, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }
}
