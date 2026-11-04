using Delobytes.App.Backend.Catalog.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Sales.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ChannelProductLinkedConsumer.
/// </summary>
public class ChannelProductLinkedMassTransitConsumer : IConsumer<ChannelProductLinkedEvent>
{
    private readonly ChannelProductLinkedConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ChannelProductLinkedMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ChannelProductLinkedMassTransitConsumer(ChannelProductLinkedConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ChannelProductLinkedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
