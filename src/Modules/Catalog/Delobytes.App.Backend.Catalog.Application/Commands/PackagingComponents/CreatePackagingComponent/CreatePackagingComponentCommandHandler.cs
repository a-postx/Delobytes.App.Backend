using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.PackagingComponents.CreatePackagingComponent;

public class CreatePackagingComponentCommandHandler : IRequestHandler<CreatePackagingComponentCommand, CreatePackagingComponentResponse>
{
    private readonly IPackagingComponentRepository _repository;
    private readonly IPackagingComponentPriceRepository _priceRepository;

    public CreatePackagingComponentCommandHandler(
        IPackagingComponentRepository repository,
        IPackagingComponentPriceRepository priceRepository)
    {
        _repository = repository;
        _priceRepository = priceRepository;
    }

    public async Task<CreatePackagingComponentResponse> Handle(CreatePackagingComponentCommand request, CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        PackagingComponent component = new PackagingComponent
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            Unit = request.Unit,
            IsActive = true,
            CreatedAt = now,
        };

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

        _repository.Add(component);
        _priceRepository.Add(price);

        // Both repositories share the scoped CatalogDbContext, so one SaveChanges persists component and price atomically.
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreatePackagingComponentResponse { Id = component.Id };
    }
}
