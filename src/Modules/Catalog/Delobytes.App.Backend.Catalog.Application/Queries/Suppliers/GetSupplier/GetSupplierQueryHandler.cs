using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Suppliers.GetSupplier;

public class GetSupplierQueryHandler : IRequestHandler<GetSupplierQuery, GetSupplierResponse?>
{
    private readonly ISupplierRepository _repository;

    public GetSupplierQueryHandler(ISupplierRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetSupplierResponse?> Handle(GetSupplierQuery request, CancellationToken cancellationToken)
    {
        Supplier? supplier = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (supplier == null)
        {
            return null;
        }

        return new GetSupplierResponse
        {
            Id = supplier.Id,
            Inn = supplier.Inn,
            Name = supplier.Name,
            Description = supplier.Description,
            Phone = supplier.Phone,
            Email = supplier.Email,
            IsActive = supplier.IsActive,
            CreatedAt = supplier.CreatedAt,
            UpdatedAt = supplier.UpdatedAt,
        };
    }
}
