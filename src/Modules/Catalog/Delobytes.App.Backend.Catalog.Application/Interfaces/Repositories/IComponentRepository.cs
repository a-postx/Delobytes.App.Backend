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

    /// <summary>
    /// Returns one page of components, with price versions and suppliers loaded, together with
    /// the total count of matching rows.
    /// </summary>
    /// <param name="skip">Rows to skip; null means none. Applied after ordering.</param>
    /// <param name="take">Rows to take; null means no limit. Applied after ordering.</param>
    /// <param name="sortBy">Sort key resolved against a fixed whitelist (name, supplier, validFrom,
    /// createdAt); unknown keys fall back to name. Sorting by supplier or validFrom uses the
    /// component's active price version (<c>Prices.FirstOrDefault(p =&gt; p.IsActive)</c>); a
    /// component without an active price sorts as if that value were null.</param>
    /// <param name="descending">True for descending order, false for ascending.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="search">Free-text filter: a component matches when its name contains the term,
    /// case-insensitively. The term is trimmed and truncated to 200 characters; null, empty or
    /// whitespace-only means "no filter". Paging is applied after this filter, so the returned
    /// total count describes the filtered set.</param>
    /// <returns>The unpaged total count and the requested slice of components.</returns>
    Task<(int TotalCount, IReadOnlyList<Component> Items)> GetPagedAsync(
        int? skip,
        int? take,
        string? sortBy,
        bool descending,
        CancellationToken ct,
        string? search = null);

    void Add(Component component);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
