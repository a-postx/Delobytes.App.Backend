using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IBomLineRepository
{
    Task<BomLine?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<BomLine>> GetActiveByProductIdAsync(Guid productId, CancellationToken ct);
    Task<IReadOnlyList<BomLine>> GetHistoryByProductIdAsync(Guid productId, CancellationToken ct);
    Task<IReadOnlyList<BomLine>> GetEffectiveAtAsync(Guid productId, DateOnly asOf, CancellationToken ct);
    Task<IReadOnlyList<Guid>> GetProductIdsByComponentIdAsync(Guid componentId, CancellationToken ct);
    void Add(BomLine line);
    Task<int> SaveChangesAsync(CancellationToken ct);
}
