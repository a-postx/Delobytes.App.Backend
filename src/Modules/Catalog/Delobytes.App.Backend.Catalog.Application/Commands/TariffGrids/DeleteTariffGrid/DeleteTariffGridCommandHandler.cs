using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.TariffGrids.DeleteTariffGrid;

public class DeleteTariffGridCommandHandler : IRequestHandler<DeleteTariffGridCommand, DeleteTariffGridResponse>
{
    private readonly ITariffGridRepository _repository;

    public DeleteTariffGridCommandHandler(ITariffGridRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteTariffGridResponse> Handle(DeleteTariffGridCommand request, CancellationToken cancellationToken)
    {
        TariffGrid? grid = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (grid == null)
        {
            return new DeleteTariffGridResponse { Found = false };
        }

        grid.IsActive = false;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteTariffGridResponse { Found = true };
    }
}
