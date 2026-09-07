using System.Transactions;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;

public class DeleteConnectionCommandHandler : IRequestHandler<DeleteConnectionCommand>
{
    private readonly IConnectionRepository _connectionRepository;
    private readonly IChannelRepository _channelRepository;

    public DeleteConnectionCommandHandler(
        IConnectionRepository connectionRepository,
        IChannelRepository channelRepository)
    {
        _connectionRepository = connectionRepository;
        _channelRepository = channelRepository;
    }

    public async Task Handle(DeleteConnectionCommand request, CancellationToken cancellationToken)
    {
        Connection? connection = await _connectionRepository
            .FindByIdWithChannelAsync(request.Id, cancellationToken);

        if (connection == null)
        {
            throw new KeyNotFoundException($"Подключение с ID '{request.Id}' не найдено.");
        }

        Channel? catalogChannel = await _channelRepository
            .GetByIdAsync(connection.ChannelId, cancellationToken);

        using (TransactionScope scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            connection.IsActive = false;

            if (catalogChannel != null)
            {
                catalogChannel.IsActive = false;
                await _channelRepository.SaveChangesAsync(cancellationToken);
            }

            await _connectionRepository.SaveChangesAsync(cancellationToken);

            scope.Complete();
        }
    }
}
