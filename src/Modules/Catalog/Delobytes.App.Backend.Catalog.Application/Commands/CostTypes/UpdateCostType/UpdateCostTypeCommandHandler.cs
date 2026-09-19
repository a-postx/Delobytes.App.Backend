using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.UpdateCostType;

public class UpdateCostTypeCommandHandler : IRequestHandler<UpdateCostTypeCommand, UpdateCostTypeResponse>
{
    private readonly ICostTypeRepository _repository;

    public UpdateCostTypeCommandHandler(ICostTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateCostTypeResponse> Handle(UpdateCostTypeCommand request, CancellationToken cancellationToken)
    {
        CostType? costType = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (costType == null)
        {
            return new UpdateCostTypeResponse { Found = false };
        }

        costType.Name = request.Name;
        costType.Description = request.Description;
        costType.IsActive = request.IsActive;
        costType.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateCostTypeResponse { Found = true };
    }
}
