using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Suppliers;

public class GetSuppliersQuery : IRequest<GetSuppliersResponse>
{
}

public class GetSuppliersResponse
{
    public IReadOnlyList<SupplierItem> Items { get; set; } = new List<SupplierItem>();
}

public class SupplierItem
{
    public Guid Id { get; set; }

    public string Inn { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

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
