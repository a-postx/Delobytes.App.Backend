using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Suppliers.UpdateSupplier;

public class UpdateSupplierCommandHandler : IRequestHandler<UpdateSupplierCommand, UpdateSupplierResponse>
{
    private readonly ISupplierRepository _repository;

    public UpdateSupplierCommandHandler(ISupplierRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateSupplierResponse> Handle(UpdateSupplierCommand request, CancellationToken cancellationToken)
    {
        Supplier? supplier = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (supplier == null)
        {
            return new UpdateSupplierResponse { Found = false };
        }

        supplier.Inn = request.Inn;
        supplier.Name = request.Name;
        supplier.Description = request.Description;
        supplier.Phone = request.Phone;
        supplier.Email = request.Email;
        supplier.IsActive = request.IsActive;
        supplier.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateSupplierResponse { Found = true };
    }
}
