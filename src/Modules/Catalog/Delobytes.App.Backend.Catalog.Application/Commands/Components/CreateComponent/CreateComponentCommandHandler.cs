using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components.CreateComponent;

public class CreateComponentCommandHandler : IRequestHandler<CreateComponentCommand, CreateComponentResponse>
{
    private readonly IComponentRepository _repository;
    private readonly IComponentPriceRepository _priceRepository;

    public CreateComponentCommandHandler(
        IComponentRepository repository,
        IComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<CreateComponentResponse> Handle(CreateComponentCommand request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Component component = new Component
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Unit = request.Unit,
            IsActive = true
        };

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

        _repository.Add(component);
        _priceRepository.Add(price);

        // Both repositories share the scoped CatalogDbContext, so one SaveChanges persists component and price atomically.
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateComponentResponse { Id = component.Id };
    }
}
