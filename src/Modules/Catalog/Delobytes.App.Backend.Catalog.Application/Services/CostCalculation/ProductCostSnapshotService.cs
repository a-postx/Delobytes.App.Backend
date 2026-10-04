using System.Text.Json;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

public class ProductCostSnapshotService : IProductCostSnapshotService
{
    private readonly IProductCostSnapshotRepository _snapshotRepository;
    private readonly ICostCalculator _costCalculator;

    public ProductCostSnapshotService(
        IProductCostSnapshotRepository snapshotRepository,
        ICostCalculator costCalculator)
    {
        _snapshotRepository = snapshotRepository;
        _costCalculator = costCalculator;
    }

    public async Task CaptureBeforeChangeAsync(IReadOnlyCollection<Guid> productIds, string triggerReason, CancellationToken ct)
    {
        if (productIds.Count == 0)
        {
            return;
        }

        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);
        HashSet<Guid> distinctProductIds = productIds.ToHashSet();
        IReadOnlySet<Guid> existingProductIds = await _snapshotRepository
            .GetExistingProductIdsAsync(distinctProductIds, today, triggerReason, ct);

        foreach (Guid productId in distinctProductIds.Except(existingProductIds))
        {
            CostBreakdown breakdown = await _costCalculator.CalculateAsync(productId, today, ct);
            ProductCostSnapshot snapshot = new ProductCostSnapshot
            {
                Id = Guid.NewGuid(),
                ProductId = productId,
                AsOfDate = breakdown.AsOfDate,
                MaterialCost = breakdown.MaterialCost,
                LogisticsCost = breakdown.LogisticsCost,
                PackagingCost = breakdown.PackagingCost,
                LaborCost = breakdown.LaborCost,
                TotalCost = breakdown.TotalCost,
                IsComplete = breakdown.IsComplete,
                LinesSnapshotJson = JsonSerializer.Serialize(breakdown.Lines),
                TriggerReason = triggerReason,
                CalculatedAt = DateTimeOffset.UtcNow,
            };

            _snapshotRepository.Add(snapshot);
        }
    }
}
