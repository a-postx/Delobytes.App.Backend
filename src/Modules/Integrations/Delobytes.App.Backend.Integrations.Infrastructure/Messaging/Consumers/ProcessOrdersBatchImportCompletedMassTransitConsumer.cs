using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

public class ProcessOrdersBatchImportCompletedMassTransitConsumer : IConsumer<OrdersBatchImportCompletedEvent>
{
    private readonly ProcessOrdersBatchImportCompletedConsumer _consumer;

    public ProcessOrdersBatchImportCompletedMassTransitConsumer(ProcessOrdersBatchImportCompletedConsumer consumer)
    {
        _consumer = consumer;
    }

    public async Task Consume(ConsumeContext<OrdersBatchImportCompletedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.MessageId ?? Guid.NewGuid(), context.CancellationToken);
    }
}
