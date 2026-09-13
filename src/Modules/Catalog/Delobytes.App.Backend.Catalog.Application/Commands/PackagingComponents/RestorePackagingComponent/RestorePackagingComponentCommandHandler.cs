using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.RestorePackagingComponent;

public class RestorePackagingComponentCommandHandler : IRequestHandler<RestorePackagingComponentCommand, RestorePackagingComponentResponse>
{
    private readonly IPackagingComponentRepository _repository;
    private readonly IPackagingComponentPriceRepository _priceRepository;

    public RestorePackagingComponentCommandHandler(
        IPackagingComponentRepository repository,
        IPackagingComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<RestorePackagingComponentResponse> Handle(RestorePackagingComponentCommand request, CancellationToken cancellationToken)
    {
        PackagingComponent? component = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (component == null)
        {
            return new RestorePackagingComponentResponse { Found = false };
        }

        component.IsActive = true;

        // Reactivate the last price version by ValidFrom. ValidFrom itself is an invariant — restoring never rewrites it.
        PackagingComponentPrice? latestPrice = await _priceRepository.GetLatestByComponentIdAsync(component.Id, cancellationToken);
        if (latestPrice != null)
        {
            latestPrice.IsActive = true;
            latestPrice.UpdatedAt = DateTimeOffset.UtcNow;
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new RestorePackagingComponentResponse { Found = true };
    }
}
