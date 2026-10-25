using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Components;

/// <summary>
/// Adds a new price version for an existing packaging component. The previous active
/// version is deactivated; no existing price record is ever overwritten.
/// </summary>
public class CreateComponentPriceCommand : IRequest<CreateComponentPriceResponse>, IRequireRole
{
    public Guid ComponentId { get; set; }

    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class CreateComponentPriceResponse
{
    public Guid Id { get; set; }

    public bool Found { get; set; }
}

public class CreateComponentPriceCommandHandler : IRequestHandler<CreateComponentPriceCommand, CreateComponentPriceResponse>
{
    private readonly IComponentRepository _repository;
    private readonly IComponentPriceRepository _priceRepository;
    private readonly IBomLineRepository _bomLineRepository;
    private readonly IProductCostSnapshotService _snapshotService;

    public CreateComponentPriceCommandHandler(
        IComponentRepository repository,
        IComponentPriceRepository priceRepository,
        IBomLineRepository bomLineRepository,
        IProductCostSnapshotService snapshotService)
    {
        _repository = repository;
        _priceRepository = priceRepository;
        _bomLineRepository = bomLineRepository;
        _snapshotService = snapshotService;
    }

    public async Task<CreateComponentPriceResponse> Handle(CreateComponentPriceCommand request, CancellationToken cancellationToken)
    {
        Component? component = await _repository.GetByIdAsync(request.ComponentId, cancellationToken);

        if (component == null)
        {
            return new CreateComponentPriceResponse { Found = false };
        }

        IReadOnlyList<Guid> affectedProductIds = await _bomLineRepository.GetProductIdsByComponentIdAsync(
            request.ComponentId, cancellationToken);

        await _snapshotService.CaptureBeforeChangeAsync(affectedProductIds, "ComponentPriceChanged", cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

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
