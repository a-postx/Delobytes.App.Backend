using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ImportProductBatchConsumer.
/// </summary>
public class ImportProductBatchMassTransitConsumer : IConsumer<ProductImportBatchRequestedEvent>
{
    private readonly ImportProductBatchConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImportProductBatchMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ImportProductBatchMassTransitConsumer(ImportProductBatchConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductImportBatchRequestedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
