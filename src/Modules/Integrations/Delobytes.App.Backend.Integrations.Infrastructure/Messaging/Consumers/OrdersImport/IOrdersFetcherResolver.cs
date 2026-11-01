using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Resolves the <see cref="IOrdersFetchers"/> for one (channel code, orders job type) pair, the
/// last seam between <c>ProcessOrdersImportConsumer</c> and the channel-specific fetch pipeline.
/// </summary>
public interface IOrdersFetcherResolver
{
    /// <summary>
    /// Attempts to resolve the fetchers for the given channel and job type.
    /// </summary>
    /// <returns><c>true</c> and the fetchers when the combination is supported; otherwise
    /// <c>false</c> and a null <paramref name="fetchers"/>.</returns>
    public bool TryResolve(string channelCode, JobType jobType, out IOrdersFetchers? fetchers);
}
