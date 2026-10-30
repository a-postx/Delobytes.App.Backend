using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

public class ProcessOrdersImportFetchCompletedMassTransitConsumer : IConsumer<OrdersImportFetchCompletedEvent>
{
    private readonly ProcessOrdersBatchImportCompletedConsumer _consumer;

    public ProcessOrdersImportFetchCompletedMassTransitConsumer(ProcessOrdersBatchImportCompletedConsumer consumer)
    {
        _consumer = consumer;
    }

    public async Task Consume(ConsumeContext<OrdersImportFetchCompletedEvent> context)
    {
        await _consumer.ProcessFetchCompletedAsync(context.Message, context.CancellationToken);
    }
}
