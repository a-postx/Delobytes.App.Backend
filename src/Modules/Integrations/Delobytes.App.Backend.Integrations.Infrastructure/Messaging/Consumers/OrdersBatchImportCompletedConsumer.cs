using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

public class OrdersBatchImportCompletedConsumer : IConsumer<OrdersBatchImportCompletedEvent>
{
    private readonly OrdersImportFinisher _finisher;

    public OrdersBatchImportCompletedConsumer(OrdersImportFinisher finisher)
    {
        _finisher = finisher;
    }

    public async Task Consume(ConsumeContext<OrdersBatchImportCompletedEvent> context)
    {
        await _finisher.ProcessAsync(context.Message, context.Message.BatchId, context.CancellationToken);
    }
}
