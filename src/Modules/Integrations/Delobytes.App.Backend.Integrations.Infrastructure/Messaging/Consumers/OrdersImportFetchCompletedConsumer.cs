using Delobytes.App.Backend.Integrations.Application.Orders;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Infrastructure.Services;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

public class OrdersImportFetchCompletedConsumer : IConsumer<OrdersImportFetchCompletedEvent>
{
    private readonly OrdersImportFinisher _finisher;

    public OrdersImportFetchCompletedConsumer(OrdersImportFinisher finisher)
    {
        _finisher = finisher;
    }

    public async Task Consume(ConsumeContext<OrdersImportFetchCompletedEvent> context)
    {
        await _finisher.ProcessFetchCompletedAsync(context.Message, context.CancellationToken);
    }
}
