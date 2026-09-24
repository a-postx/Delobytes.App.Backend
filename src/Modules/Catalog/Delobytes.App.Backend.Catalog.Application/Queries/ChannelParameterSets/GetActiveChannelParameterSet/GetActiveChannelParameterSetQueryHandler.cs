using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ChannelParameterSets.GetActiveChannelParameterSet;

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
            CommissionPercent = active.CommissionPercent,
            AcquiringPercent = active.AcquiringPercent,
            SppPercent = active.SppPercent,
            SppEnabled = active.SppEnabled,
            ValidFrom = active.ValidFrom,
            CreatedAt = active.CreatedAt,
        };
    }
}
