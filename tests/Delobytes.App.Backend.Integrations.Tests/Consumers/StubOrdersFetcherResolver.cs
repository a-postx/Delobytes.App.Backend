using System;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Delobytes.App.Backend.Integrations.Application.Orders;

namespace Delobytes.App.Backend.Integrations.Tests.Consumers;

/// <summary>
/// Resolver double that answers exactly one (channel code, job type) pair from a caller-supplied
/// fetcher set, so a consumer test runs one channel in isolation without building the container.
/// </summary>
internal sealed class StubOrdersFetcherResolver : IOrdersFetcherResolver
{
    private readonly IOrdersFetchers? _fetchers;
    private readonly JobType _jobType;
    private readonly string _channelCode;

    private StubOrdersFetcherResolver(string channelCode, JobType jobType, IOrdersFetchers? fetchers)
    {
        _channelCode = channelCode;
        _jobType = jobType;
        _fetchers = fetchers;
    }

    /// <summary>
    /// Builds a resolver over a caller-supplied fetcher set.
    /// </summary>
    public static StubOrdersFetcherResolver For(string channelCode, JobType jobType, IOrdersFetchers fetchers)
    {
        return new StubOrdersFetcherResolver(channelCode, jobType, fetchers);
    }

    /// <summary>
    /// A resolver that supports nothing, for asserting the unsupported-combination failure.
    /// </summary>
    public static StubOrdersFetcherResolver Unsupported()
    {
        return new StubOrdersFetcherResolver(string.Empty, JobType.ProductsImport, null);
    }

    /// <inheritdoc/>
    public OrdersFetchersResolution Resolve(string channelCode, JobType jobType)
    {
        if (_fetchers != null
            && string.Equals(channelCode, _channelCode, StringComparison.OrdinalIgnoreCase)
            && jobType == _jobType)
        {
            return new OrdersFetchersResolution.Supported
            {
                Fetchers = _fetchers,
            };
        }

        return new OrdersFetchersResolution.Unsupported
        {
            Reason = $"Channel '{channelCode}' does not support orders import for job type '{jobType}'",
        };
    }
}
