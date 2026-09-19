using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.CostTypes.CreateCostType;

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
