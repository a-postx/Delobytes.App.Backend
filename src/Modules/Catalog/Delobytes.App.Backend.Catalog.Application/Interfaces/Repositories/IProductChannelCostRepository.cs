using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IProductChannelCostRepository
{
    Task<ProductChannelCost?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<ProductChannelCost>> GetByProductAndChannelAsync(
        Guid productId, Guid channelId, CancellationToken ct);

    Task<IReadOnlyList<ProductChannelCost>> GetByProductAsync(Guid productId, CancellationToken ct);

    void Add(ProductChannelCost cost);

    void Remove(ProductChannelCost cost);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
