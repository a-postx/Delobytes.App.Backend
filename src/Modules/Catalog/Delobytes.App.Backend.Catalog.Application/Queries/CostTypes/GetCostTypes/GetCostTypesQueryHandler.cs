using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.CostTypes.GetCostTypes;

public class GetCostTypesQueryHandler : IRequestHandler<GetCostTypesQuery, GetCostTypesResponse>
{
    private readonly ICostTypeRepository _repository;

    public GetCostTypesQueryHandler(ICostTypeRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetCostTypesResponse> Handle(GetCostTypesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<CostType> costTypes = await _repository.GetAllAsync(cancellationToken);

        return new GetCostTypesResponse
        {
            Items = costTypes.Select(ct => new CostTypeItem
            {
                Id = ct.Id,
                Name = ct.Name,
                Description = ct.Description,
                IsActive = ct.IsActive,
                CreatedAt = ct.CreatedAt,
            }).ToList(),
        };
    }
}
