using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ChannelProductFullSetReconciliationConsumer.
/// </summary>
public class ChannelProductFullSetReconciliationMassTransitConsumer
    : IConsumer<ChannelProductFullSetReconciliationRequestedEvent>
{
    private readonly ChannelProductFullSetReconciliationConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="ChannelProductFullSetReconciliationMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ChannelProductFullSetReconciliationMassTransitConsumer(
        ChannelProductFullSetReconciliationConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<ChannelProductFullSetReconciliationRequestedEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
