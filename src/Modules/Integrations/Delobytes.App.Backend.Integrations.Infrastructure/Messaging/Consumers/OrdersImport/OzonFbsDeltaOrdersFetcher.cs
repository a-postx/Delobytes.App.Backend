using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Application.Models;
using Delobytes.App.Backend.Integrations.Application.Options;
using Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The Ozon FBS delta branch: <see cref="OzonDeltaOrdersFetcher"/> built for
/// <see cref="OrdersSchema.Fbs"/>. Separate from <see cref="OzonDeltaOrdersFetcher"/> because the
/// statuses a delta window requests are endpoint-specific, and a wrapper rather than a second
/// registration of the base type, which DI could not tell apart from the FBO one.
/// </summary>
public sealed class OzonFbsDeltaOrdersFetcher : IOrdersFetcher
{
    private readonly OzonDeltaOrdersFetcher _inner;

    public OzonFbsDeltaOrdersFetcher(
        OzonFbsOrdersReader windowReader,
        ISyncCheckpointRepository syncCheckpointRepository,
        OrdersBatchPublisher batchPublisher,
        ILogger<OzonDeltaOrdersFetcher> logger,
        IOptions<OzonImportOptions> options)
    {
        _inner = new OzonDeltaOrdersFetcher(
            windowReader,
            syncCheckpointRepository,
            batchPublisher,
            logger,
            options,
            OrdersSchema.Fbs,
            OzonPostingStatuses.FbsStatuses);
    }

    /// <inheritdoc/>
    public Task<FetchOutcome> FetchAsync(
        OrdersImportContext context, OrdersRunProgress progress, CancellationToken cancellationToken)
    {
        return _inner.FetchAsync(context, progress, cancellationToken);
    }
}
