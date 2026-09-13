using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.DeletePackagingComponent;

public class DeletePackagingComponentCommandHandler : IRequestHandler<DeletePackagingComponentCommand, DeletePackagingComponentResponse>
{
    private readonly IPackagingComponentRepository _repository;
    private readonly IPackagingComponentPriceRepository _priceRepository;

    public DeletePackagingComponentCommandHandler(
        IPackagingComponentRepository repository,
        IPackagingComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<DeletePackagingComponentResponse> Handle(DeletePackagingComponentCommand request, CancellationToken cancellationToken)
    {
        PackagingComponent? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new DeletePackagingComponentResponse { Found = false };
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Soft delete — mark as inactive
        component.IsActive = false;

        // Deactivate the current price version so the component drops out of cost calculations.
        // ValidFrom stays untouched, so the archived history keeps its ordering.
        PackagingComponentPrice? activePrice = await _priceRepository.GetActiveByComponentIdAsync(component.Id, cancellationToken);
        if (activePrice != null)
        {
            activePrice.IsActive = false;
            activePrice.UpdatedAt = now;
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeletePackagingComponentResponse { Found = true };
    }
}
