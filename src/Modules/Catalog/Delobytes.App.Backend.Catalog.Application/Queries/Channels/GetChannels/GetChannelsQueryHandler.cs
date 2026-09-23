using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Channels.GetChannels;

public class GetChannelsQueryHandler : IRequestHandler<GetChannelsQuery, GetChannelsResponse>
{
    private readonly IChannelRepository _repository;

    public GetChannelsQueryHandler(IChannelRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetChannelsResponse> Handle(GetChannelsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Channel> channels = await _repository.GetAllAsync(cancellationToken);

        List<ChannelDto> items = channels
            .Select(c => new ChannelDto
            {
                Id = c.Id,
                Name = c.Name,
                SystemChannelTemplateId = c.SystemChannelTemplateId,
                IsCustom = c.IsCustom,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
            })
            .ToList();

        return new GetChannelsResponse { Items = items };
    }
}
