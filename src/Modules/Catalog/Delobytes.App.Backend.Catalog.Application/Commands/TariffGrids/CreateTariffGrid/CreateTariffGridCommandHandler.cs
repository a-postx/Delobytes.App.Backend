using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.CreateTariffGrid;

public class CreateTariffGridCommandHandler : IRequestHandler<CreateTariffGridCommand, CreateTariffGridResponse>
{
    private readonly ITariffGridRepository _repository;

    public CreateTariffGridCommandHandler(ITariffGridRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateTariffGridResponse> Handle(CreateTariffGridCommand request, CancellationToken cancellationToken)
    {
        TariffGrid grid = new TariffGrid
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            TariffType = request.TariffType,
            ValidFrom = request.ValidFrom,
            ChannelId = request.ChannelId,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        foreach (TariffGridEntryRequest entry in request.Entries)
        {
            grid.Entries.Add(new TariffGridEntry
            {
                Id = Guid.NewGuid(),
                TariffGridId = grid.Id,
                RegionOrCity = entry.RegionOrCity,
                VolumeThresholdLiters = entry.VolumeThresholdLiters,
                Rate = entry.Rate,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _repository.Add(grid);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateTariffGridResponse { Id = grid.Id };
    }
}
