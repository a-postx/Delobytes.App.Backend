using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrids;

public class GetTariffGridsQueryHandler : IRequestHandler<GetTariffGridsQuery, GetTariffGridsResponse>
{
    private readonly ITariffGridRepository _repository;

    public GetTariffGridsQueryHandler(ITariffGridRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetTariffGridsResponse> Handle(GetTariffGridsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<TariffGrid> grids = request.TariffType.HasValue
            ? await _repository.GetByTypeAsync(request.TariffType.Value, cancellationToken)
            : await _repository.GetAllAsync(cancellationToken);

        return new GetTariffGridsResponse
        {
            Items = grids.Select(g => new TariffGridItem
            {
                Id = g.Id,
                Name = g.Name,
                TariffType = g.TariffType,
                ValidFrom = g.ValidFrom,
                ChannelId = g.ChannelId,
                IsActive = g.IsActive,
                CreatedAt = g.CreatedAt,
            }).ToList(),
        };
    }
}
