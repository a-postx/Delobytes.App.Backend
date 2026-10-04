using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.CaptureProductCostSnapshot;

public class CaptureProductCostSnapshotCommandHandler : IRequestHandler<CaptureProductCostSnapshotCommand, CaptureProductCostSnapshotResponse>
{
    private readonly IProductRepository _productRepository;
    private readonly IProductCostSnapshotService _snapshotService;

    public CaptureProductCostSnapshotCommandHandler(
        IProductRepository productRepository,
        IProductCostSnapshotService snapshotService)
    {
        _productRepository = productRepository;
        _snapshotService = snapshotService;
    }

    public async Task<CaptureProductCostSnapshotResponse> Handle(
        CaptureProductCostSnapshotCommand request,
        CancellationToken cancellationToken)
    {
        Product? product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product == null)
        {
            return new CaptureProductCostSnapshotResponse { Found = false };
        }

        await _snapshotService.CaptureBeforeChangeAsync(
            new[] { request.ProductId },
            "Manual",
            cancellationToken);
        await _productRepository.SaveChangesAsync(cancellationToken);

        return new CaptureProductCostSnapshotResponse { Found = true };
    }
}
