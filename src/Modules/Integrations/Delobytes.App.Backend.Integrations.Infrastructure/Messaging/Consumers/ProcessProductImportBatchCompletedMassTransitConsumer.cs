using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ProcessProductImportBatchCompletedConsumer.
/// </summary>
public class ProcessProductImportBatchCompletedMassTransitConsumer : IConsumer<ProductImportBatchCompletedEvent>
{
    private readonly ProcessProductImportBatchCompletedConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessProductImportBatchCompletedMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ProcessProductImportBatchCompletedMassTransitConsumer(ProcessProductImportBatchCompletedConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ProductImportBatchCompletedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
