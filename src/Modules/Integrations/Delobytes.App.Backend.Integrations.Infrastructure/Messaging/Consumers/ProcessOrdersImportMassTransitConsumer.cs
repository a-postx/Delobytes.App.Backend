using Delobytes.App.Backend.Integrations.Application.Events;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

public class ProcessOrdersImportMassTransitConsumer : IConsumer<OrdersImportRequestedEvent>
{
    private readonly ProcessOrdersImportConsumer _consumer;

    public ProcessOrdersImportMassTransitConsumer(ProcessOrdersImportConsumer consumer)
    {
        _consumer = consumer;
    }

    public async Task Consume(ConsumeContext<OrdersImportRequestedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
