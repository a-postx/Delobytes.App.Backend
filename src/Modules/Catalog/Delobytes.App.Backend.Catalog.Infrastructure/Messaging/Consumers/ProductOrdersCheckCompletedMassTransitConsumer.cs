using Delobytes.App.Backend.Sales.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ProductOrdersCheckCompletedConsumer.
/// </summary>
public class ProductOrdersCheckCompletedMassTransitConsumer : IConsumer<ProductOrdersCheckCompletedEvent>
{
    private readonly ProductOrdersCheckCompletedConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductOrdersCheckCompletedMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ProductOrdersCheckCompletedMassTransitConsumer(ProductOrdersCheckCompletedConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductOrdersCheckCompletedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
