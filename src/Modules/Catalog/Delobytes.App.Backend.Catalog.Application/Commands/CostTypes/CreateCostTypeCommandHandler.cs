using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes;

public class CreateCostTypeCommand : IRequest<CreateCostTypeResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class CreateCostTypeResponse
{
    public Guid Id { get; set; }
}

public class CreateCostTypeCommandHandler : IRequestHandler<CreateCostTypeCommand, CreateCostTypeResponse>
{
    private readonly ICostTypeRepository _repository;

    public CreateCostTypeCommandHandler(ICostTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateCostTypeResponse> Handle(CreateCostTypeCommand request, CancellationToken cancellationToken)
    {
        CostType costType = new CostType
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(costType);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateCostTypeResponse { Id = costType.Id };
    }
}
