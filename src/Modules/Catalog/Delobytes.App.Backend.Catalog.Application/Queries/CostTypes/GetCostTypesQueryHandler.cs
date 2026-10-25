using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.CostTypes;

public class GetCostTypesQuery : IRequest<GetCostTypesResponse>
{
}

public class GetCostTypesResponse
{
    public IReadOnlyList<CostTypeItem> Items { get; set; } = new List<CostTypeItem>();
}

public class CostTypeItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

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
