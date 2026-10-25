using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components;

public class CreateComponentCommand : IRequest<CreateComponentResponse>, IRequireRole
{
    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Domain.Enums.Unit Unit { get; set; }

    /// <summary>Категория компонента. По умолчанию — материал.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;

    /// <summary>Price of the first price version created together with the component.</summary>
    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    /// <summary>Effective date of the first price version.</summary>
    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class CreateComponentResponse
{
    public Guid Id { get; set; }
}

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
            Category = request.Category,
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
