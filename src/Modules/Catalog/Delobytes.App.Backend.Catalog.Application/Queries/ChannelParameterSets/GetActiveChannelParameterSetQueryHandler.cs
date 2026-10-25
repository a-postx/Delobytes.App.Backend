using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets;

public class GetActiveChannelParameterSetQuery : IRequest<GetActiveChannelParameterSetResponse>
{
    public Guid ChannelId { get; set; }
}

public class GetActiveChannelParameterSetResponse
{
    public Guid Id { get; set; }
    public Guid ChannelId { get; set; }
    public DateOnly ValidFrom { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public bool Found { get; set; }
}

public class GetActiveChannelParameterSetQueryHandler : IRequestHandler<GetActiveChannelParameterSetQuery, GetActiveChannelParameterSetResponse>
{
    private readonly IChannelParameterSetRepository _repository;

    public GetActiveChannelParameterSetQueryHandler(IChannelParameterSetRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetActiveChannelParameterSetResponse> Handle(
        GetActiveChannelParameterSetQuery request,
        CancellationToken cancellationToken)
    {
        ChannelParameterSet? active = await _repository.GetActiveByChannelIdAsync(request.ChannelId, cancellationToken);

        if (active == null)
        {
            return new GetActiveChannelParameterSetResponse { Found = false };
        }

        return new GetActiveChannelParameterSetResponse
        {
            Found = true,
            Id = active.Id,
            ChannelId = active.ChannelId,
            ValidFrom = active.ValidFrom,
            CreatedAt = active.CreatedAt,
        };
    }
}
