using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Channels.CreateChannel;

public class CreateChannelCommandHandler : IRequestHandler<CreateChannelCommand, CreateChannelResponse>
{
    private readonly IChannelRepository _repository;

    public CreateChannelCommandHandler(IChannelRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateChannelResponse> Handle(CreateChannelCommand request, CancellationToken cancellationToken)
    {
        Channel channel = new Channel
        {
            Id = Guid.NewGuid(),
            SystemChannelTemplateId = request.SystemChannelTemplateId,
            Name = request.Name,
            CustomApiUrl = request.CustomApiUrl,
            IsCustom = request.SystemChannelTemplateId == null,
            IsActive = true
        };

        _repository.Add(channel);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateChannelResponse { Id = channel.Id };
    }
}
