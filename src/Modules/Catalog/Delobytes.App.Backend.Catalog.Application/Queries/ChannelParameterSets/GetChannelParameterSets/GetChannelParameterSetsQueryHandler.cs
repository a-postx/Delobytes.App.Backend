using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetChannelParameterSets;

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
