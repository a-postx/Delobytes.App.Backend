using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit adapter for <see cref="ConnectionCreatedEventConsumer"/>.
/// </summary>
public class ConnectionCreatedEventMassTransitConsumer : IConsumer<ConnectionCreatedEvent>
{
    private readonly ConnectionCreatedEventConsumer _consumer;

    public ConnectionCreatedEventMassTransitConsumer(ConnectionCreatedEventConsumer consumer)
    {
        _consumer = consumer;
    }

    public async Task Consume(ConsumeContext<ConnectionCreatedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
