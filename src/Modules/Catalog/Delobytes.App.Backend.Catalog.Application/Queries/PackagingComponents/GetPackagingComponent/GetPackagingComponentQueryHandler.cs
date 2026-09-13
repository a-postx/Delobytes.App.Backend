using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents.GetPackagingComponent;

public class GetPackagingComponentQueryHandler : IRequestHandler<GetPackagingComponentQuery, GetPackagingComponentResponse?>
{
    private readonly IPackagingComponentRepository _repository;

    public GetPackagingComponentQueryHandler(IPackagingComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPackagingComponentResponse?> Handle(GetPackagingComponentQuery request, CancellationToken cancellationToken)
    {
        PackagingComponent? component = await _repository.GetWithPricesByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return null;
        }

        return new GetPackagingComponentResponse
        {
            Id = component.Id,
            Name = component.Name,
            Description = component.Description,
            Unit = component.Unit,
            ActivePrice = PackagingComponentPriceMapper.Map(component.Prices.FirstOrDefault(p => p.IsActive)),
            IsActive = component.IsActive,
            CreatedAt = component.CreatedAt,
        };
    }
}
