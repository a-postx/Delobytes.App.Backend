using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSuppliers;

public class GetSuppliersQueryHandler : IRequestHandler<GetSuppliersQuery, GetSuppliersResponse>
{
    private readonly ISupplierRepository _repository;

    public GetSuppliersQueryHandler(ISupplierRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetSuppliersResponse> Handle(GetSuppliersQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Supplier> suppliers = await _repository.GetAllAsync(cancellationToken);

        return new GetSuppliersResponse
        {
            Items = suppliers.Select(s => new SupplierItem
            {
                Id = s.Id,
                Inn = s.Inn,
                Name = s.Name,
                Description = s.Description,
                Phone = s.Phone,
                Email = s.Email,
                IsActive = s.IsActive,
                CreatedAt = s.CreatedAt,
            }).ToList(),
        };
    }
}
