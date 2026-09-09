using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.UpdateTariffGrid;

public class UpdateTariffGridCommandHandler : IRequestHandler<UpdateTariffGridCommand, UpdateTariffGridResponse>
{
    private readonly ITariffGridRepository _repository;

    public UpdateTariffGridCommandHandler(ITariffGridRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateTariffGridResponse> Handle(UpdateTariffGridCommand request, CancellationToken cancellationToken)
    {
        TariffGrid? grid = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (grid == null)
        {
            return new UpdateTariffGridResponse { Found = false };
        }

        grid.Name = request.Name;
        grid.IsActive = request.IsActive;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateTariffGridResponse { Found = true };
    }
}
