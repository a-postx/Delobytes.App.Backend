using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.RestoreComponent;

public class RestoreComponentCommandHandler : IRequestHandler<RestoreComponentCommand, RestoreComponentResponse>
{
    private readonly IComponentRepository _repository;
    private readonly IComponentPriceRepository _priceRepository;

    public RestoreComponentCommandHandler(
        IComponentRepository repository,
        IComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<RestoreComponentResponse> Handle(RestoreComponentCommand request, CancellationToken cancellationToken)
    {
        Component? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new RestoreComponentResponse { Found = false };
        }

        component.IsActive = true;

        // Reactivate the last price version by ValidFrom. ValidFrom itself is an invariant — restoring never rewrites it.
        ComponentPrice? latestPrice = await _priceRepository.GetLatestByComponentIdAsync(component.Id, cancellationToken);
        if (latestPrice != null)
        {
            latestPrice.IsActive = true;
            latestPrice.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new RestoreComponentResponse { Found = true };
    }
}
