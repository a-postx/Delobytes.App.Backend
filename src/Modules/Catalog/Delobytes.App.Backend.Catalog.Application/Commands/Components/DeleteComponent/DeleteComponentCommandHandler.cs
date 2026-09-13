using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.DeleteComponent;

public class DeleteComponentCommandHandler : IRequestHandler<DeleteComponentCommand, DeleteComponentResponse>
{
    private readonly IComponentRepository _repository;
    private readonly IComponentPriceRepository _priceRepository;

    public DeleteComponentCommandHandler(
        IComponentRepository repository,
        IComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<DeleteComponentResponse> Handle(DeleteComponentCommand request, CancellationToken cancellationToken)
    {
        Component? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new DeleteComponentResponse { Found = false };
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Soft delete — mark as inactive
        component.IsActive = false;

        // Deactivate the current price version so the component drops out of cost calculations.
        // ValidFrom stays untouched, so the archived history keeps its ordering.
        ComponentPrice? activePrice = await _priceRepository.GetActiveByComponentIdAsync(component.Id, cancellationToken);
        if (activePrice != null)
        {
            activePrice.IsActive = false;
            activePrice.UpdatedAt = now;
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteComponentResponse { Found = true };
    }
}
