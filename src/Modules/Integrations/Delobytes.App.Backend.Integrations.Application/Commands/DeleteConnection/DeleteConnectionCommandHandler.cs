using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Integrations.Application.Commands.DeleteConnection;

/// <summary>
/// Deactivates a connection (soft delete). The associated Catalog.Channel is intentionally
/// NOT touched — Channel is an independent business entity owned by Catalog and its lifecycle
/// no longer depends on connection status. Historical data collected while the connection was
/// active remains fully accessible for the channel.
/// </summary>
public class DeleteConnectionCommandHandler : IRequestHandler<DeleteConnectionCommand>
{
    private readonly IConnectionRepository _connectionRepository;

    public DeleteConnectionCommandHandler(IConnectionRepository connectionRepository)
    {
        _connectionRepository = connectionRepository;
    }

    public async Task Handle(DeleteConnectionCommand request, CancellationToken cancellationToken)
    {
        Connection? connection = await _connectionRepository
            .FindByIdWithTemplateAsync(request.Id, cancellationToken);

        if (connection == null)
        {
            throw new KeyNotFoundException($"Подключение с ID '{request.Id}' не найдено.");
        }

        connection.IsActive = false;
        connection.UpdatedAt = DateTimeOffset.UtcNow;

        await _connectionRepository.SaveChangesAsync(cancellationToken);
    }
}
