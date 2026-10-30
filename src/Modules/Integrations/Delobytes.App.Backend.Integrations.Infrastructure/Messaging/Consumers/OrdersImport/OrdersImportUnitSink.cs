using Delobytes.App.Backend.Integrations.Contracts.Models;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Receives one fetched unit of orders (one backfill day, or the whole delta window) for publication.
/// </summary>
internal interface IOrdersImportUnitSink
{
    public Task PublishAsync(IReadOnlyList<ChannelOrderSnapshot> unitOrders, CancellationToken cancellationToken);
}

/// <summary>
/// Binds the run-scoped <see cref="JobRun"/>, <see cref="Connection"/> and batch cursor so the
/// publisher can be handed to code that has no business knowing about them.
/// </summary>
internal sealed class OrdersImportUnitSink : IOrdersImportUnitSink
{
    private readonly OrdersBatchPublisher _publisher;
    private readonly JobRun _jobRun;
    private readonly Connection _connection;
    private readonly OrdersImportBatchCursor _cursor;

    public OrdersImportUnitSink(
        OrdersBatchPublisher publisher,
        JobRun jobRun,
        Connection connection,
        OrdersImportBatchCursor cursor)
    {
        _publisher = publisher;
        _jobRun = jobRun;
        _connection = connection;
        _cursor = cursor;
    }

    public Task PublishAsync(IReadOnlyList<ChannelOrderSnapshot> unitOrders, CancellationToken cancellationToken)
    {
        return _publisher.PublishUnitAsync(_jobRun, _connection, _cursor, unitOrders, cancellationToken);
    }
}
