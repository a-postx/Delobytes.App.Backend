using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponentPrice;

public class CreateComponentPriceCommandHandler : IRequestHandler<CreateComponentPriceCommand, CreateComponentPriceResponse>
{
    private readonly IComponentRepository _repository;
    private readonly IComponentPriceRepository _priceRepository;

    public CreateComponentPriceCommandHandler(
        IComponentRepository repository,
        IComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<CreateComponentPriceResponse> Handle(CreateComponentPriceCommand request, CancellationToken cancellationToken)
    {
        Component? component = await _repository.GetByIdAsync(request.ComponentId, cancellationToken);

        if (component == null)
        {
            return new CreateComponentPriceResponse { Found = false };
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        // Deactivation and insertion go through the shared scoped DbContext and one SaveChanges,
        // so the component is never left without an active price version.
        ComponentPrice? activePrice = await _priceRepository.GetActiveByComponentIdAsync(component.Id, cancellationToken);
        if (activePrice != null)
        {
            activePrice.IsActive = false;
            activePrice.UpdatedAt = now;
        }

        ComponentPrice price = new ComponentPrice
        {
            Id = Guid.NewGuid(),
            ComponentId = component.Id,
            PricePerUnit = request.PricePerUnit,
            SupplierId = request.SupplierId,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = now,
        };

        _priceRepository.Add(price);
        await _priceRepository.SaveChangesAsync(cancellationToken);

        return new CreateComponentPriceResponse { Id = price.Id, Found = true };
    }
}
