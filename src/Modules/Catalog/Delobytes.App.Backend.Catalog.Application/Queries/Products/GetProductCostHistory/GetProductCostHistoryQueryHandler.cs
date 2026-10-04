using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostHistory;

public class GetProductCostHistoryQueryHandler : IRequestHandler<GetProductCostHistoryQuery, GetProductCostHistoryResponse>
{
    private readonly IProductRepository _productRepository;
    private readonly IProductCostSnapshotRepository _snapshotRepository;

    public GetProductCostHistoryQueryHandler(
        IProductRepository productRepository,
        IProductCostSnapshotRepository snapshotRepository)
    {
        _productRepository = productRepository;
        _snapshotRepository = snapshotRepository;
    }

    public async Task<GetProductCostHistoryResponse> Handle(GetProductCostHistoryQuery request, CancellationToken cancellationToken)
    {
        Product? product = await _productRepository.GetByIdAsync(request.ProductId, cancellationToken);
        if (product == null)
        {
            return new GetProductCostHistoryResponse { Found = false };
        }

        int skip = Math.Max(request.Skip, 0);
        int take = Math.Clamp(request.Take, 1, 100);
        (int totalCount, IReadOnlyList<ProductCostSnapshot> snapshots) = await _snapshotRepository
            .GetHistoryAsync(request.ProductId, skip, take, cancellationToken);

        return new GetProductCostHistoryResponse
        {
            Found = true,
            TotalCount = totalCount,
            Items = snapshots.Select(snapshot => new ProductCostSnapshotDto
            {
                Id = snapshot.Id,
                ProductId = snapshot.ProductId,
                AsOfDate = snapshot.AsOfDate,
                MaterialCost = snapshot.MaterialCost,
                LogisticsCost = snapshot.LogisticsCost,
                PackagingCost = snapshot.PackagingCost,
                LaborCost = snapshot.LaborCost,
                TotalCost = snapshot.TotalCost,
                IsComplete = snapshot.IsComplete,
                LinesSnapshotJson = snapshot.LinesSnapshotJson,
                TriggerReason = snapshot.TriggerReason,
                CalculatedAt = snapshot.CalculatedAt,
            }).ToList(),
        };
    }
}
