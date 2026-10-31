using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;

/// <summary>
/// Adapts the existing <see cref="IWildberriesRateLimiter"/> (paced per (API key, method) from the
/// API's own response headers) to the channel-neutral <see cref="IOrdersRateLimiter"/> contract.
/// <see cref="RateLimitScope.Method"/> and <see cref="RateLimitScope.ChannelCode"/> are ignored: the
/// orders endpoint is always <see cref="WildberriesApiMethod.SupplierOrders"/>, the only method this
/// adapter is ever asked to pace.
/// </summary>
public sealed class WildberriesOrdersRateLimiterAdapter : IOrdersRateLimiter
{
    private readonly IWildberriesRateLimiter _wildberriesRateLimiter;

    /// <summary>
    /// Initializes a new instance of the <see cref="WildberriesOrdersRateLimiterAdapter"/> class.
    /// </summary>
    public WildberriesOrdersRateLimiterAdapter(IWildberriesRateLimiter wildberriesRateLimiter)
    {
        _wildberriesRateLimiter = wildberriesRateLimiter;
    }

    /// <inheritdoc/>
    public Task<RateLimitAcquireResult> TryAcquireAsync(RateLimitScope scope, CancellationToken cancellationToken)
    {
        return _wildberriesRateLimiter.TryAcquireAsync(
            scope.ApiKey, WildberriesApiMethod.SupplierOrders, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset> ReportRateLimitedAsync(
        RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
    {
        return _wildberriesRateLimiter.ReportRateLimitedAsync(
            scope.ApiKey, WildberriesApiMethod.SupplierOrders, signal, cancellationToken);
    }

    /// <inheritdoc/>
    public Task ReportSuccessAsync(RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
    {
        return _wildberriesRateLimiter.ReportSuccessAsync(
            scope.ApiKey, WildberriesApiMethod.SupplierOrders, signal, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
        RateLimitScope scope, CancellationToken cancellationToken)
    {
        return _wildberriesRateLimiter.GetNextRequestAllowedAtAsync(
            scope.ApiKey, WildberriesApiMethod.SupplierOrders, cancellationToken);
    }
}
