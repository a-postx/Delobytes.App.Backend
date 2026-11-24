using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Suppliers;

public class DeleteSupplierCommand : IRequest<DeleteSupplierResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class DeleteSupplierResponse
{
}

public class DeleteSupplierCommandHandler : IRequestHandler<DeleteSupplierCommand, DeleteSupplierResponse>
{
    private readonly ISupplierRepository _repository;

    public DeleteSupplierCommandHandler(ISupplierRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteSupplierResponse> Handle(DeleteSupplierCommand request, CancellationToken cancellationToken)
    {
        Supplier? supplier = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (supplier == null)
        {
            throw new AppException(CatalogErrorCodes.SupplierNotFound);
        }

        // Soft delete — deactivate rather than remove so existing references remain intact
        supplier.IsActive = false;
        supplier.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteSupplierResponse();
    }
}
