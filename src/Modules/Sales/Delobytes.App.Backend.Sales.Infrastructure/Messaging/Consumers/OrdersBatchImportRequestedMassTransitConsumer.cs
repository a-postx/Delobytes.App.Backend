using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for OrdersBatchImportRequestedConsumer.
/// </summary>
public class OrdersBatchImportRequestedMassTransitConsumer : IConsumer<OrdersBatchImportRequestedEvent>
{
    private readonly OrdersBatchImportRequestedConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersBatchImportRequestedMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public OrdersBatchImportRequestedMassTransitConsumer(OrdersBatchImportRequestedConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<OrdersBatchImportRequestedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
