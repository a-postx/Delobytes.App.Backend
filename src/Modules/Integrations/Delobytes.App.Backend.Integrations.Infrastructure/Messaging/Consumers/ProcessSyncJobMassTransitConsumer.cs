using Delobytes.App.Backend.Integrations.Application.Events;
using MassTransit;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// MassTransit consumer adapter for ProcessSyncJobConsumer.
/// </summary>
public class ProcessSyncJobMassTransitConsumer : IConsumer<StartSyncJobEvent>
{
    private readonly ProcessSyncJobConsumer _consumer;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessSyncJobMassTransitConsumer"/> class.
    /// </summary>
    /// <param name="consumer">Business logic consumer.</param>
    public ProcessSyncJobMassTransitConsumer(ProcessSyncJobConsumer consumer)
    {
        _consumer = consumer;
    }

    /// <inheritdoc/>
    public async Task Consume(ConsumeContext<StartSyncJobEvent> context)
    {
        await _consumer.ProcessAsync(context.Message, context.CancellationToken);
    }
}
