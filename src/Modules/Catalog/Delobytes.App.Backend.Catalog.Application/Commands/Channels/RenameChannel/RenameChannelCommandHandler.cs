using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Channels.RenameChannel;

public class RenameChannelCommandHandler : IRequestHandler<RenameChannelCommand, RenameChannelResponse>
{
    private readonly IChannelRepository _repository;

    public RenameChannelCommandHandler(IChannelRepository repository)
    {
        _repository = repository;
    }

    public async Task<RenameChannelResponse> Handle(RenameChannelCommand request, CancellationToken cancellationToken)
    {
        Channel? channel = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (channel == null)
        {
            return new RenameChannelResponse { Found = false };
        }

        channel.Name = request.Name;
        channel.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new RenameChannelResponse { Found = true };
    }
}
