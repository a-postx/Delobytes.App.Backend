using System.Text.Json;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

public class ProductCostSnapshotService : IProductCostSnapshotService
{
    private readonly IProductCostSnapshotRepository _snapshotRepository;
    private readonly ICostCalculator _costCalculator;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantTimeService _tenantTimeService;
    private readonly TimeProvider _timeProvider;

    public ProductCostSnapshotService(
        IProductCostSnapshotRepository snapshotRepository,
        ICostCalculator costCalculator,
        ITenantContext tenantContext,
        ITenantTimeService tenantTimeService,
        TimeProvider timeProvider)
    {
        _snapshotRepository = snapshotRepository;
        _costCalculator = costCalculator;
        _tenantContext = tenantContext;
        _tenantTimeService = tenantTimeService;
        _timeProvider = timeProvider;
    }

    public async Task CaptureBeforeChangeAsync(IReadOnlyCollection<Guid> productIds, string triggerReason, CancellationToken ct)
    {
        if (productIds.Count == 0)
        {
            return;
        }

        // AsOfDate снимка — календарная дата тенанта: стоимость «на сегодня» в 01:00 по
        // Самаре ещё относится ко вчерашней UTC-дате.
        DateOnly today = await _tenantTimeService.GetTodayAsync(_tenantContext, ct);
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
                CalculatedAt = _timeProvider.GetUtcNow(),
            };

            _snapshotRepository.Add(snapshot);
        }
    }
}
