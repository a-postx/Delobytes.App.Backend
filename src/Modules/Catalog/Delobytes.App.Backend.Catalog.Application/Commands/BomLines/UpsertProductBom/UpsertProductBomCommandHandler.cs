using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines.UpsertProductBom;

public class UpsertProductBomCommandHandler : IRequestHandler<UpsertProductBomCommand, UpsertProductBomResponse>
{
    private readonly IBomLineRepository _repository;
    private readonly IComponentRepository _componentRepository;
    private readonly IProductCostSnapshotService _snapshotService;

    public UpsertProductBomCommandHandler(
        IBomLineRepository repository,
        IComponentRepository componentRepository,
        IProductCostSnapshotService snapshotService)
    {
        _repository = repository;
        _componentRepository = componentRepository;
        _snapshotService = snapshotService;
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
        DateOnly today = DateOnly.FromDateTime(now.UtcDateTime);

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
