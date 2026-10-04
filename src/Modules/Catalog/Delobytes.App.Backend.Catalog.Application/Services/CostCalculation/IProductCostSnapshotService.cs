namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

public interface IProductCostSnapshotService
{
    Task CaptureBeforeChangeAsync(IReadOnlyCollection<Guid> productIds, string triggerReason, CancellationToken ct);
}
