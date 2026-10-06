using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

public interface IComponentRepository
{
    Task<Component?> GetByIdAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// Loads the components with the given identifiers in a single round trip.
    /// An empty <paramref name="ids"/> returns an empty list without querying the database.
    /// The order of the result is not guaranteed, so callers that care about it must restore it themselves.
    /// </summary>
    Task<IReadOnlyList<Component>> GetByIdsAsync(IReadOnlyList<Guid> ids, CancellationToken ct);

    /// <summary>Loads the component together with its price versions.</summary>
    Task<Component?> GetWithPricesByIdAsync(Guid id, CancellationToken ct);

    Task<IReadOnlyList<Component>> GetAllAsync(CancellationToken ct);

    void Add(Component component);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
