using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets;

public class GetChannelParameterSetsQuery : IRequest<GetChannelParameterSetsResponse>
{
    public Guid ChannelId { get; set; }
}

public class GetChannelParameterSetsResponse
{
    public List<ChannelParameterSetDto> Items { get; set; } = new List<ChannelParameterSetDto>();
}

public class ChannelParameterSetDto
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public class GetChannelParameterSetsQueryHandler : IRequestHandler<GetChannelParameterSetsQuery, GetChannelParameterSetsResponse>
{
    private readonly IChannelParameterSetRepository _repository;

    public GetChannelParameterSetsQueryHandler(IChannelParameterSetRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetChannelParameterSetsResponse> Handle(
        GetChannelParameterSetsQuery request,
        CancellationToken cancellationToken)
    {
        List<ChannelParameterSetDto> items = (await _repository.GetByChannelIdAsync(request.ChannelId, cancellationToken))
            .Select(cps => new ChannelParameterSetDto
            {
                Id = cps.Id,
                ChannelId = cps.ChannelId,
                ValidFrom = cps.ValidFrom,
                CreatedAt = cps.CreatedAt,
            })
            .ToList();

        return new GetChannelParameterSetsResponse { Items = items };
    }
}
