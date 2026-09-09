using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using MassTransit;

namespace Delobytes.App.Backend.Catalog.Infrastructure.Messaging.Consumers;

/// <summary>
/// Creates a Catalog Channel when Integrations reports a new connection.
/// </summary>
public class ConnectionCreatedEventConsumer : IConsumer<ConnectionCreatedEvent>
{
    private readonly IChannelRepository _channelRepository;

    public ConnectionCreatedEventConsumer(IChannelRepository channelRepository)
    {
        _channelRepository = channelRepository;
    }

    public async Task Consume(ConsumeContext<ConnectionCreatedEvent> context)
    {
        ConnectionCreatedEvent evt = context.Message;

        Channel channel = new Channel
        {
            Id = evt.ChannelId,
            SystemChannelTemplateId = evt.SystemChannelTemplateId,
            Name = evt.ChannelName,
            IsCustom = false,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _channelRepository.Add(channel);
        await _channelRepository.SaveChangesAsync(context.CancellationToken);
    }
}
