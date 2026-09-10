using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IRawMaterialRateRepository
{
    Task<RawMaterialRate?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<RawMaterialRate>> GetAllAsync(CancellationToken ct);

    Task<IReadOnlyList<RawMaterialRate>> GetByProductIdAsync(Guid productId, CancellationToken ct);

    void Add(RawMaterialRate rate);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
