using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;
using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// <see cref="IOrdersSource"/> double that serves fixtures instead of calling Wildberries over
/// HTTP, but - like <c>WildberriesApiClient</c> - gates every call through a real
/// <see cref="IWildberriesRateLimiter"/> and reports the outcome back to it. Lets a
/// consumer-level test exercise the real adaptive pacing end to end without an HTTP transport.
/// </summary>
internal sealed class FakeRateLimitedOrdersSource : IChannelApiClient, IOrdersSource
{
    private readonly IWildberriesRateLimiter _rateLimiter;
    private readonly string _apiKey;
    private readonly Func<OrdersRequest, List<ChannelOrderSnapshot>> _fixtureProvider;

    public FakeRateLimitedOrdersSource(
        IWildberriesRateLimiter rateLimiter,
        string apiKey,
        Func<OrdersRequest, List<ChannelOrderSnapshot>> fixtureProvider)
    {
        _rateLimiter = rateLimiter;
        _apiKey = apiKey;
        _fixtureProvider = fixtureProvider;
    }

    public string ChannelCode => "wildberries";

    public int CallCount { get; private set; }

    public async Task<OrdersPage> GetOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken)
    {
        CallCount++;

        bool acquired = await _rateLimiter.TryAcquireAsync(
            _apiKey, WildberriesApiMethod.SupplierOrders, cancellationToken);

        if (!acquired)
        {
            return new OrdersPage { Throttled = true };
        }

        List<ChannelOrderSnapshot> orders = _fixtureProvider(request);

        await _rateLimiter.ReportSuccessAsync(
            _apiKey,
            WildberriesApiMethod.SupplierOrders,
            new RateLimitSignal { Limit = 10, Remaining = 9, StatusCode = 200 },
            cancellationToken);

        return new OrdersPage { Orders = orders, HasMore = false };
    }

    public Task<AccountInfo?> GetAccountInfoAsync(
        Guid? connectionId,
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct)
    {
        throw new NotSupportedException("Not exercised by the real-rate-limiter backfill test.");
    }

    public Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(
        Guid connectionId, ProductCardsCursor? cursor, int limit, CancellationToken ct)
    {
        throw new NotSupportedException("Not exercised by the real-rate-limiter backfill test.");
    }
}
