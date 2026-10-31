using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.RateLimiting;

/// <summary>
/// Dispatches <see cref="IOrdersRateLimiter"/> calls to the channel-specific implementation keyed
/// by <see cref="RateLimitScope.ChannelCode"/>. This is the single <see cref="IOrdersRateLimiter"/>
/// registered in DI; channel API clients never see the concrete per-channel limiter.
/// </summary>
public sealed class OrdersRateLimiterRouter : IOrdersRateLimiter
{
    private const string WildberriesChannelCode = "wildberries";
    private const string OzonChannelCode = "ozon";

    private readonly WildberriesOrdersRateLimiterAdapter _wildberriesRateLimiter;
    private readonly OzonRateLimiter _ozonRateLimiter;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersRateLimiterRouter"/> class.
    /// </summary>
    public OrdersRateLimiterRouter(
        WildberriesOrdersRateLimiterAdapter wildberriesRateLimiter, OzonRateLimiter ozonRateLimiter)
    {
        _wildberriesRateLimiter = wildberriesRateLimiter;
        _ozonRateLimiter = ozonRateLimiter;
    }

    /// <inheritdoc/>
    public Task<RateLimitAcquireResult> TryAcquireAsync(RateLimitScope scope, CancellationToken cancellationToken)
    {
        return Resolve(scope.ChannelCode).TryAcquireAsync(scope, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset> ReportRateLimitedAsync(
        RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
    {
        return Resolve(scope.ChannelCode).ReportRateLimitedAsync(scope, signal, cancellationToken);
    }

    /// <inheritdoc/>
    public Task ReportSuccessAsync(RateLimitScope scope, RateLimitSignal signal, CancellationToken cancellationToken)
    {
        return Resolve(scope.ChannelCode).ReportSuccessAsync(scope, signal, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<DateTimeOffset?> GetNextRequestAllowedAtAsync(
        RateLimitScope scope, CancellationToken cancellationToken)
    {
        return Resolve(scope.ChannelCode).GetNextRequestAllowedAtAsync(scope, cancellationToken);
    }

    private IOrdersRateLimiter Resolve(string channelCode)
    {
        return channelCode switch
        {
            WildberriesChannelCode => _wildberriesRateLimiter,
            OzonChannelCode => _ozonRateLimiter,
            _ => throw new NotSupportedException(
                $"No orders rate limiter is configured for channel '{channelCode}'.")
        };
    }
}
