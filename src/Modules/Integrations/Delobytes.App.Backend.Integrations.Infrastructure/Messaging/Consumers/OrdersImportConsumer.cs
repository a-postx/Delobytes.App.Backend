using Delobytes.App.Backend.Integrations.Application.Events;
using Delobytes.App.Backend.Integrations.Application.Orders;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit adapter for the orders import run; the work itself belongs to
/// <see cref="OrdersImportOrchestrator"/>.
/// </summary>
public class OrdersImportConsumer : IConsumer<OrdersImportRequestedEvent>
{
    private readonly OrdersImportOrchestrator _orchestrator;

    /// <summary>
    /// Initializes a new instance of the <see cref="OrdersImportConsumer"/> class.
    /// </summary>
    /// <param name="orchestrator">Orders import orchestrator.</param>
    public OrdersImportConsumer(OrdersImportOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<OrdersImportRequestedEvent> context)
    {
        await _orchestrator.ProcessAsync(context.Message, context.CancellationToken);
    }
}
