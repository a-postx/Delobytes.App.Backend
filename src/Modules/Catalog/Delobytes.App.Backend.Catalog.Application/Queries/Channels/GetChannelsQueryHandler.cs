using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Channels;

public class GetChannelsQuery : IRequest<GetChannelsResponse>
{
}

public class ChannelDto
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public Guid? SystemChannelTemplateId { get; set; }

    /// <summary>System channel template code ("wildberries", "ozon", "yandex.kit"), or null for a custom channel.</summary>
    public string? Code { get; set; }

    public bool IsCustom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

public class GetChannelsResponse
{
    public List<ChannelDto> Items { get; set; } = new List<ChannelDto>();
}

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
                Code = c.Code,
                IsCustom = c.IsCustom,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
            })
            .ToList();

        return new GetChannelsResponse { Items = items };
    }
}
