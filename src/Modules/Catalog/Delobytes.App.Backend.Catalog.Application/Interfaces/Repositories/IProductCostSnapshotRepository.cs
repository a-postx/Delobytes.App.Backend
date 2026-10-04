using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductCostSnapshotRepository
{
    Task<IReadOnlySet<Guid>> GetExistingProductIdsAsync(
        IReadOnlyCollection<Guid> productIds,
        DateOnly asOfDate,
        string triggerReason,
        CancellationToken ct);

    Task<(int TotalCount, IReadOnlyList<ProductCostSnapshot> Items)> GetHistoryAsync(
        Guid productId,
        int skip,
        int take,
        CancellationToken ct);

    void Add(ProductCostSnapshot snapshot);
}
