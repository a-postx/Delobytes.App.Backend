using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Contracts.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines;

public class UpsertProductBomCommand : IRequest<UpsertProductBomResponse>, IRequireRole
{
    public Guid ProductId { get; set; }
    public List<UpsertProductBomItem> Lines { get; set; } = new List<UpsertProductBomItem>();
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class UpsertProductBomItem
{
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
}

public class UpsertProductBomResponse
{
    public int Count { get; set; }
}

public class UpsertProductBomCommandHandler : IRequestHandler<UpsertProductBomCommand, UpsertProductBomResponse>
{
    private readonly IBomLineRepository _repository;
    private readonly IComponentRepository _componentRepository;
    private readonly IProductCostSnapshotService _snapshotService;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantTimeService _tenantTimeService;

    public UpsertProductBomCommandHandler(
        IBomLineRepository repository,
        IComponentRepository componentRepository,
        IProductCostSnapshotService snapshotService,
        ITenantContext tenantContext,
        ITenantTimeService tenantTimeService)
    {
        _repository = repository;
        _componentRepository = componentRepository;
        _snapshotService = snapshotService;
        _tenantContext = tenantContext;
        _tenantTimeService = tenantTimeService;
    }

    public async Task<UpsertProductBomResponse> Handle(UpsertProductBomCommand request, CancellationToken cancellationToken)
    {
        if (request.Lines.Any(x => x.Quantity <= 0))
        {
            throw new AppException(ErrorCodes.Catalog.BomLineInvalidQuantity);
        }

        if (request.Lines.Select(x => x.ComponentId).Distinct().Count() != request.Lines.Count)
        {
            throw new AppException(ErrorCodes.Common.ValidationFailed);
        }

        foreach (UpsertProductBomItem item in request.Lines)
        {
            if (await _componentRepository.GetByIdAsync(item.ComponentId, cancellationToken) == null)
            {
                throw new AppException(ErrorCodes.Catalog.BomComponentNotFound);
            }
        }

        await _snapshotService.CaptureBeforeChangeAsync(new[] { request.ProductId }, "BomChanged", cancellationToken);

        IReadOnlyList<BomLine> active = await _repository.GetActiveByProductIdAsync(request.ProductId, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        // ValidFrom — календарная дата тенанта: UTC-дата на несколько часов отстаёт от
        // местной и сдвинула бы вступление состава в силу на предыдущий день.
        DateOnly today = await _tenantTimeService.GetTodayAsync(_tenantContext, cancellationToken);

        foreach (BomLine line in active)
        {
            line.IsActive = false;
            line.UpdatedAt = now;
        }

        foreach (UpsertProductBomItem item in request.Lines)
        {
            _repository.Add(new BomLine {
                Id = Guid.NewGuid(),
                ProductId = request.ProductId,
                ComponentId = item.ComponentId,
                Quantity = item.Quantity,
                ValidFrom = today,
                IsActive = true,
                CreatedAt = now });
        }

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpsertProductBomResponse { Count = request.Lines.Count };
    }
}
