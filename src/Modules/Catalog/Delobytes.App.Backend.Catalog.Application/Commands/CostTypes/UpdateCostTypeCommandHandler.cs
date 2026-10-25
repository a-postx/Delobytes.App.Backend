using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes;

public class UpdateCostTypeCommand : IRequest<UpdateCostTypeResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class UpdateCostTypeResponse
{
    public bool Found { get; set; }
}

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
