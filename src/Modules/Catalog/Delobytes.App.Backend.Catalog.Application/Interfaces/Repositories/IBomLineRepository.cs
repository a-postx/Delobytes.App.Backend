using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IBomLineRepository
{
    public Task<BomLine?> GetByIdAsync(Guid id, CancellationToken ct);
    public Task<IReadOnlyList<BomLine>> GetActiveByProductIdAsync(Guid productId, CancellationToken ct);
    public Task<IReadOnlyList<BomLine>> GetHistoryByProductIdAsync(Guid productId, CancellationToken ct);
    public Task<IReadOnlyList<BomLine>> GetEffectiveAtAsync(Guid productId, DateOnly asOf, CancellationToken ct);
    public Task<IReadOnlyList<Guid>> GetProductIdsByComponentIdAsync(Guid componentId, CancellationToken ct);
    public void Add(BomLine line);
    public Task<int> SaveChangesAsync(CancellationToken ct);
}
