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

    /// <summary>
    /// Returns one page of components, with price versions and suppliers loaded, together with
    /// the total count of matching rows.
    /// </summary>
    /// <param name="isActive">Status filter; null means "all components".</param>
    /// <param name="skip">Rows to skip; null means none. Applied after ordering.</param>
    /// <param name="take">Rows to take; null means no limit. Applied after ordering.</param>
    /// <param name="sortBy">Sort key resolved against a fixed whitelist (name, supplier, validFrom,
    /// createdAt); unknown keys fall back to name. Sorting by supplier or validFrom uses the
    /// component's active price version (<c>Prices.FirstOrDefault(p =&gt; p.IsActive)</c>);
    /// a component without an active price always sorts last in both directions.</param>
    /// <param name="descending">True for descending order, false for ascending.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="search">Free-text filter applied on top of <paramref name="isActive"/> with AND:
    /// a component matches when its name contains the term, case-insensitively. The term is trimmed
    /// and truncated to 200 characters; null, empty or whitespace-only means "no filter". Paging is
    /// applied after this filter, so the returned total count describes the filtered set.</param>
    /// <returns>The unpaged total count and the requested slice of components.</returns>
    Task<(int TotalCount, IReadOnlyList<Component> Items)> GetPagedAsync(
        bool? isActive,
        int? skip,
        int? take,
        string? sortBy,
        bool descending,
        CancellationToken ct,
        string? search = null);

    /// <summary>
    /// Returns how many components exist per status tab:
    /// Active, Inactive and the total across both.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="search">The same free-text filter the list query was given, so the tab counters
    /// describe the current search result set instead of the whole catalog: a component is counted
    /// when its name contains the trimmed, 200-character-truncated term, case-insensitively. Null,
    /// empty or whitespace-only means "count everything".</param>
    Task<(int Active, int Inactive, int All)> GetStatusCountsAsync(CancellationToken ct, string? search = null);

    void Add(Component component);

    Task<int> SaveChangesAsync(CancellationToken ct);
}
