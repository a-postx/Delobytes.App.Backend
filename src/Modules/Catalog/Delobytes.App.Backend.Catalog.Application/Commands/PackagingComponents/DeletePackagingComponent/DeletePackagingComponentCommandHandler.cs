using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.DeletePackagingComponent;

public class DeletePackagingComponentCommandHandler : IRequestHandler<DeletePackagingComponentCommand, DeletePackagingComponentResponse>
{
    private readonly IPackagingComponentRepository _repository;

    public DeletePackagingComponentCommandHandler(IPackagingComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeletePackagingComponentResponse> Handle(DeletePackagingComponentCommand request, CancellationToken cancellationToken)
    {
        PackagingComponent? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new DeletePackagingComponentResponse { Found = false };
        }

        // Soft delete — mark as inactive
        component.IsActive = false;
        component.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeletePackagingComponentResponse { Found = true };
    }
}
