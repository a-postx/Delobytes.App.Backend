using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface ICostTypeRepository
{
    Task<CostType?> GetByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<CostType>> GetAllAsync(CancellationToken ct);

    void Add(CostType costType);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
