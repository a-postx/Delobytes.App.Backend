using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Sales.Application.Orders;
using MassTransit;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// Consumer for <see cref="OrdersBatchImportRequestedEvent"/>.
/// </summary>
public class OrdersBatchImportRequestedConsumer : IConsumer<OrdersBatchImportRequestedEvent>
{
    private readonly OrdersBatchImporter _importer;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersBatchImportRequestedConsumer"/> class.
    /// </summary>
    /// <param name="importer">Application-service that imports the batch.</param>
    public OrdersBatchImportRequestedConsumer(OrdersBatchImporter importer)
    {
        _importer = importer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<OrdersBatchImportRequestedEvent> context)
    {
        await _importer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
