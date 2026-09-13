using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IComponentRepository
{
    Task<Component?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>Loads the component together with its price versions.</summary>
    Task<Component?> GetWithPricesByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Component>> GetAllAsync(CancellationToken ct);

    void Add(Component component);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
