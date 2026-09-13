using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponent;

public class GetComponentQueryHandler : IRequestHandler<GetComponentQuery, GetComponentResponse?>
{
    private readonly IComponentRepository _repository;

    public GetComponentQueryHandler(IComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetComponentResponse?> Handle(GetComponentQuery request, CancellationToken cancellationToken)
    {
        Component? component = await _repository.GetWithPricesByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return null;
        }

        return new GetComponentResponse
        {
            Id = component.Id,
            Name = component.Name,
            Description = component.Description,
            Unit = component.Unit,
            ActivePrice = ComponentPriceMapper.Map(component.Prices.FirstOrDefault(p => p.IsActive)),
            IsActive = component.IsActive,
            CreatedAt = component.CreatedAt,
        };
    }
}
