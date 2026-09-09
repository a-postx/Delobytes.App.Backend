using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Deactivates a Catalog Channel when Integrations reports a connection deactivation.
/// </summary>
public class ConnectionDeactivatedEventConsumer : IConsumer<ConnectionDeactivatedEvent>
{
    private readonly IChannelRepository _channelRepository;

    public ConnectionDeactivatedEventConsumer(IChannelRepository channelRepository)
    {
        _channelRepository = channelRepository;
    }

    public async Task Consume(ConsumeContext<ConnectionDeactivatedEvent> context)
    {
        ConnectionDeactivatedEvent evt = context.Message;

        Channel? channel = await _channelRepository.GetByIdAsync(evt.ChannelId, context.CancellationToken);

        if (channel == null)
        {
            return;
        }

        channel.IsActive = false;
        await _channelRepository.SaveChangesAsync(context.CancellationToken);
    }
}
