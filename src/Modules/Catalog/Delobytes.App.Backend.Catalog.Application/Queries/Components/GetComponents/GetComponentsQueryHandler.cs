using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;

public class GetComponentsQueryHandler : IRequestHandler<GetComponentsQuery, GetComponentsResponse>
{
    private readonly IComponentRepository _repository;

    public GetComponentsQueryHandler(IComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetComponentsResponse> Handle(GetComponentsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Component> components = await _repository.GetAllAsync(cancellationToken);

        return new GetComponentsResponse
        {
            Items = components.Select(c => new ComponentItem
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Unit = c.Unit,
                ActivePrice = ComponentPriceMapper.Map(c.Prices.FirstOrDefault(p => p.IsActive)),
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
            }).ToList(),
        };
    }
}
