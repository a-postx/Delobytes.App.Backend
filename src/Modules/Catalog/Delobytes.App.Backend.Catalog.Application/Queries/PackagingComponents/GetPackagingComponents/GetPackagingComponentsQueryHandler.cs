using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.PackagingComponents.GetPackagingComponents;

public class GetPackagingComponentsQueryHandler : IRequestHandler<GetPackagingComponentsQuery, GetPackagingComponentsResponse>
{
    private readonly IPackagingComponentRepository _repository;

    public GetPackagingComponentsQueryHandler(IPackagingComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetPackagingComponentsResponse> Handle(GetPackagingComponentsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<PackagingComponent> components = await _repository.GetAllAsync(cancellationToken);

        return new GetPackagingComponentsResponse
        {
            Items = components.Select(c => new PackagingComponentItem
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Unit = c.Unit,
                PricePerUnit = c.PricePerUnit,
                SupplierId = c.SupplierId,
                SupplierName = c.Supplier?.Name,
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
            }).ToList(),
        };
    }
}
