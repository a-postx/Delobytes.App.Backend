using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ProcessProductsImportConsumer.
/// </summary>
public class ProcessProductsImportMassTransitConsumer : IConsumer<ProductsImportRequestedEvent>
{
    private readonly ProcessProductsImportConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessProductsImportMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ProcessProductsImportMassTransitConsumer(ProcessProductsImportConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductsImportRequestedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
