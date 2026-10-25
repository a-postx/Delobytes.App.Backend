using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Channels;

public class RenameChannelCommand : IRequest<RenameChannelResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class RenameChannelResponse
{
    public bool Found { get; set; }
}

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
        ////channel.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new RenameChannelResponse { Found = true };
    }
}
