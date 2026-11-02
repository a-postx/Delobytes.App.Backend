using Delobytes.App.Backend.Integrations.Domain.Enums;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Default <see cref="IOrdersFetcherResolver"/>: Wildberries supports <see cref="JobType.OrdersImport"/>,
/// Ozon supports <see cref="JobType.OrdersImportFbo"/> and <see cref="JobType.OrdersImportFbs"/>, each
/// against its own fetcher set. Any other combination is unsupported.
/// </summary>
public sealed class OrdersFetcherResolver : IOrdersFetcherResolver
{
    private const string WildberriesChannelCode = "wildberries";
    private const string OzonChannelCode = "ozon";

    private readonly WildberriesOrdersFetchers _wildberries;
    private readonly OzonOrdersFetchers _ozonFbo;
    private readonly OzonFbsOrdersFetchers _ozonFbs;

    public OrdersFetcherResolver(
        WildberriesOrdersFetchers wildberries,
        OzonOrdersFetchers ozonFbo,
        OzonFbsOrdersFetchers ozonFbs)
    {
        _wildberries = wildberries;
        _ozonFbo = ozonFbo;
        _ozonFbs = ozonFbs;
    }

    /// <inheritdoc/>
    public bool TryResolve(string channelCode, JobType jobType, out IOrdersFetchers? fetchers)
    {
        if (string.Equals(channelCode, WildberriesChannelCode, StringComparison.OrdinalIgnoreCase)
            && jobType == JobType.OrdersImport)
        {
            fetchers = _wildberries;
            return true;
        }

        if (string.Equals(channelCode, OzonChannelCode, StringComparison.OrdinalIgnoreCase))
        {
            if (jobType == JobType.OrdersImportFbo)
            {
                fetchers = _ozonFbo;
                return true;
            }

            if (jobType == JobType.OrdersImportFbs)
            {
                fetchers = _ozonFbs;
                return true;
            }
        }

        fetchers = null;
        return false;
    }
}
