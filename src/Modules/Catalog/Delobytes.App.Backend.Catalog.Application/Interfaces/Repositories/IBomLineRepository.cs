using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IBomLineRepository
{
    public Task<BomLine?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Returns the versions currently in force, that is those whose <c>ValidTo</c> is null.
    /// </summary>
    public Task<IReadOnlyList<BomLine>> GetOpenByProductIdAsync(Guid productId, CancellationToken ct);

    public Task<IReadOnlyList<BomLine>> GetHistoryByProductIdAsync(Guid productId, CancellationToken ct);

    /// <summary>
    /// Returns one version per component: the latest one whose <c>[ValidFrom, ValidTo)</c> interval
    /// covers <paramref name="asOf"/>. A component removed from the composition is absent from the
    /// result for every date from its <c>ValidTo</c> onwards.
    /// </summary>
    public Task<IReadOnlyList<BomLine>> GetEffectiveAtAsync(Guid productId, DateOnly asOf, CancellationToken ct);

    public Task<IReadOnlyList<Guid>> GetProductIdsByComponentIdAsync(Guid componentId, CancellationToken ct);

    public void Add(BomLine line);

    public Task<int> SaveChangesAsync(CancellationToken ct);
}
