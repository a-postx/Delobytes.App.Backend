using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.UpdatePackagingComponent;

public class UpdatePackagingComponentCommandHandler : IRequestHandler<UpdatePackagingComponentCommand, UpdatePackagingComponentResponse>
{
    private readonly IPackagingComponentRepository _repository;

    public UpdatePackagingComponentCommandHandler(IPackagingComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdatePackagingComponentResponse> Handle(UpdatePackagingComponentCommand request, CancellationToken cancellationToken)
    {
        PackagingComponent? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new UpdatePackagingComponentResponse { Found = false };
        }

        component.Name = request.Name;
        component.Description = request.Description;
        component.Unit = request.Unit;
        component.PricePerUnit = request.PricePerUnit;
        component.SupplierId = request.SupplierId;
        component.IsActive = request.IsActive;
        component.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdatePackagingComponentResponse { Found = true };
    }
}
