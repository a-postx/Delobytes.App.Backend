using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Resolves the <see cref="IOrdersFetchers"/> for one (channel code, orders job type) pair, the
/// last seam between <c>ProcessOrdersImportConsumer</c> and the channel-specific fetch pipeline.
/// </summary>
public interface IOrdersFetcherResolver
{
    /// <summary>
    /// Resolves the fetchers for the given channel and job type.
    /// </summary>
    public OrdersFetchersResolution Resolve(string channelCode, JobType jobType);
}

/// <summary>
/// The outcome of resolving a (channel, job type) pair. An unsupported pair carries the reason the
/// orchestrator records as the run's error, so that text has one author.
/// </summary>
public abstract record OrdersFetchersResolution
{
    public sealed record Supported : OrdersFetchersResolution
    {
        public required IOrdersFetchers Fetchers { get; init; }
    }

    public sealed record Unsupported : OrdersFetchersResolution
    {
        public required string Reason { get; init; }
    }
}
