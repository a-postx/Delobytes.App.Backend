namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

public interface IProductCostSnapshotService
{
    public Task CaptureBeforeChangeAsync(IReadOnlyCollection<Guid> productIds, string triggerReason, CancellationToken ct);
}
