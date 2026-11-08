using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Resolves the fetchers registered for a (channel code, orders job type) pair from the keyed
/// container. A pair without a registration is unsupported, and the reason it reports is exactly
/// what the orchestrator records as the run's error.
/// </summary>
public sealed class OrdersFetcherResolver : IOrdersFetcherResolver
{
    private readonly IServiceProvider _serviceProvider;

    public OrdersFetcherResolver(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public OrdersFetchersResolution Resolve(string channelCode, JobType jobType)
    {
        string key = OrdersFetcherKeys.Fetchers(channelCode.ToLowerInvariant(), jobType);

        IOrdersFetchers? fetchers = _serviceProvider.GetKeyedService<IOrdersFetchers>(key);

        if (fetchers == null)
        {
            return new OrdersFetchersResolution.Unsupported
            {
                Reason = $"Channel '{channelCode}' does not support orders import for job type '{jobType}'",
            };
        }

        return new OrdersFetchersResolution.Supported
        {
            Fetchers = fetchers,
        };
    }
}
