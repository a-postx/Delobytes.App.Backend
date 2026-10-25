using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components;

public class GetComponentQuery : IRequest<GetComponentResponse?>
{
    public Guid Id { get; set; }
}

public class GetComponentResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Domain.Enums.Unit Unit { get; set; }

    /// <summary>Категория компонента, определяющая статью себестоимости.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;

    /// <summary>Currently active price version, or null when the component has no active price.</summary>
    public ComponentPriceDto? ActivePrice { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

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
            Category = component.Category,
            ActivePrice = ComponentPriceMapper.Map(component.Prices.FirstOrDefault(p => p.IsActive)),
            IsActive = component.IsActive,
            CreatedAt = component.CreatedAt,
        };
    }
}
