using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Contracts.Events;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;

public class DeleteConnectionCommandHandler : IRequestHandler<DeleteConnectionCommand>
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IEventPublisher _eventPublisher;

    public DeleteConnectionCommandHandler(
        IConnectionRepository connectionRepository,
        IEventPublisher eventPublisher)
    {
        _connectionRepository = connectionRepository;
        _eventPublisher = eventPublisher;
    }

    public async Task Handle(DeleteConnectionCommand request, CancellationToken cancellationToken)
    {
        Connection? connection = await _connectionRepository
            .FindByIdWithChannelAsync(request.Id, cancellationToken);

        if (connection == null)
        {
            throw new KeyNotFoundException($"Подключение с ID '{request.Id}' не найдено.");
        }

        connection.IsActive = false;
        await _connectionRepository.SaveChangesAsync(cancellationToken);

        // Catalog subscribes to this event and deactivates the corresponding Channel.
        ConnectionDeactivatedEvent channelDeactivatedEvent = new ConnectionDeactivatedEvent
        {
            ChannelId = connection.ChannelId,
        };

        await _eventPublisher.PublishAsync(channelDeactivatedEvent, cancellationToken);
    }
}
