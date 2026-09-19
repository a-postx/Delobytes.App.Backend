using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.DeleteCostType;

public class DeleteCostTypeCommandHandler : IRequestHandler<DeleteCostTypeCommand, DeleteCostTypeResponse>
{
    private readonly ICostTypeRepository _repository;

    public DeleteCostTypeCommandHandler(ICostTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteCostTypeResponse> Handle(DeleteCostTypeCommand request, CancellationToken cancellationToken)
    {
        CostType? costType = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (costType == null)
        {
            return new DeleteCostTypeResponse { Found = false };
        }

        costType.IsActive = false;
        costType.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteCostTypeResponse { Found = true };
    }
}
