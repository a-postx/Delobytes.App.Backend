using Delobytes.App.Backend.Catalog.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ProductDeletionRequestedConsumer.
/// </summary>
public class ProductDeletionRequestedMassTransitConsumer : IConsumer<ProductDeletionRequestedEvent>
{
    private readonly ProductDeletionRequestedConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProductDeletionRequestedMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ProductDeletionRequestedMassTransitConsumer(ProductDeletionRequestedConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductDeletionRequestedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
