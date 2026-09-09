using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.TariffGrids.GetTariffGrid;

public class GetTariffGridQueryHandler : IRequestHandler<GetTariffGridQuery, GetTariffGridResponse?>
{
    private readonly ITariffGridRepository _repository;

    public GetTariffGridQueryHandler(ITariffGridRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetTariffGridResponse?> Handle(GetTariffGridQuery request, CancellationToken cancellationToken)
    {
        TariffGrid? grid = await _repository.GetByIdWithEntriesAsync(request.Id, cancellationToken);

        if (grid == null)
        {
            return null;
        }

        return new GetTariffGridResponse
        {
            Id = grid.Id,
            Name = grid.Name,
            TariffType = grid.TariffType,
            ValidFrom = grid.ValidFrom,
            ChannelId = grid.ChannelId,
            IsActive = grid.IsActive,
            CreatedAt = grid.CreatedAt,
            Entries = grid.Entries.Select(e => new TariffGridEntryResponse
            {
                Id = e.Id,
                RegionOrCity = e.RegionOrCity,
                VolumeThresholdLiters = e.VolumeThresholdLiters,
                Rate = e.Rate,
            }).ToList(),
        };
    }
}
