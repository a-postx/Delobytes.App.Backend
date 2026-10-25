using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Suppliers;

public class CreateSupplierCommand : IRequest<CreateSupplierResponse>, IRequireRole
{
    public string Inn { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class CreateSupplierResponse
{
    public Guid Id { get; set; }
}

public class CreateSupplierCommandHandler : IRequestHandler<CreateSupplierCommand, CreateSupplierResponse>
{
    private readonly ISupplierRepository _repository;

    public CreateSupplierCommandHandler(ISupplierRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateSupplierResponse> Handle(CreateSupplierCommand request, CancellationToken cancellationToken)
    {
        Supplier supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            Inn = request.Inn,
            Name = request.Name,
            Description = request.Description,
            Phone = request.Phone,
            Email = request.Email,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(supplier);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateSupplierResponse { Id = supplier.Id };
    }
}
