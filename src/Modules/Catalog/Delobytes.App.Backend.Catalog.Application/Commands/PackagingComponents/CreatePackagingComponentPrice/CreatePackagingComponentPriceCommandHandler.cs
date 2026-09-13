using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponentPrice;

public class CreatePackagingComponentPriceCommandHandler : IRequestHandler<CreatePackagingComponentPriceCommand, CreatePackagingComponentPriceResponse>
{
    private readonly IPackagingComponentRepository _repository;
    private readonly IPackagingComponentPriceRepository _priceRepository;

    public CreatePackagingComponentPriceCommandHandler(
        IPackagingComponentRepository repository,
        IPackagingComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<CreatePackagingComponentPriceResponse> Handle(CreatePackagingComponentPriceCommand request, CancellationToken cancellationToken)
    {
        PackagingComponent? component = await _repository.GetByIdAsync(request.PackagingComponentId, cancellationToken);

        if (component == null)
        {
            return new CreatePackagingComponentPriceResponse { Found = false };
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Deactivation and insertion go through the shared scoped DbContext and one SaveChanges,
        // so the component is never left without an active price version.
        PackagingComponentPrice? activePrice = await _priceRepository.GetActiveByComponentIdAsync(component.Id, cancellationToken);
        if (activePrice != null)
        {
            activePrice.IsActive = false;
            activePrice.UpdatedAt = now;
        }

        PackagingComponentPrice price = new PackagingComponentPrice
        {
            Id = Guid.NewGuid(),
            PackagingComponentId = component.Id,
            PricePerUnit = request.PricePerUnit,
            SupplierId = request.SupplierId,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = now,
        };

        _priceRepository.Add(price);
        await _priceRepository.SaveChangesAsync(cancellationToken);

        return new CreatePackagingComponentPriceResponse { Id = price.Id, Found = true };
    }
}
