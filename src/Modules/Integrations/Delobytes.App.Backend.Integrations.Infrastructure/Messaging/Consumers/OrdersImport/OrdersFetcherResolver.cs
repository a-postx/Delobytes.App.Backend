using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Domain.Enums;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Default <see cref="IOrdersFetcherResolver"/>: Wildberries supports <see cref="JobType.OrdersImport"/>,
/// Ozon supports <see cref="JobType.OrdersImportFbo"/> always and <see cref="JobType.OrdersImportFbs"/>
/// only while <see cref="OzonImportOptions.EnableFbs"/> is set. Any other combination is unsupported.
/// </summary>
public sealed class OrdersFetcherResolver : IOrdersFetcherResolver
{
    private const string WildberriesChannelCode = "wildberries";
    private const string OzonChannelCode = "ozon";

    private readonly WildberriesOrdersFetchers _wildberries;
    private readonly OzonOrdersFetchers _ozon;
    private readonly IOptions<OzonImportOptions> _ozonOptions;

    public OrdersFetcherResolver(
        WildberriesOrdersFetchers wildberries,
        OzonOrdersFetchers ozon,
        IOptions<OzonImportOptions> ozonOptions)
    {
        _wildberries = wildberries;
        _ozon = ozon;
        _ozonOptions = ozonOptions;
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
                fetchers = _ozon;
                return true;
            }

            if (jobType == JobType.OrdersImportFbs && _ozonOptions.Value.EnableFbs)
            {
                fetchers = _ozon;
                return true;
            }
        }

        fetchers = null;
        return false;
    }
}
