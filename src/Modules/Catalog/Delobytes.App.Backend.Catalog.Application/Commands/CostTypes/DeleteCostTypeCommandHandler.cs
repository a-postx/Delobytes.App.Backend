using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes;

public class DeleteCostTypeCommand : IRequest<DeleteCostTypeResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class DeleteCostTypeResponse
{
    public bool Found { get; set; }
}

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
