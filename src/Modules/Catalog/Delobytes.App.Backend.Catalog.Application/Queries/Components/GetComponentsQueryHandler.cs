using MediatR;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components;

/// <summary>
/// Returns the component list, optionally filtered by search term, sorted, and optionally paged.
/// </summary>
/// <remarks>
/// Paging is opt-in: when <see cref="Page"/> is null the query reproduces the original behaviour
/// and returns the whole matching list without <c>Skip</c>/<c>Take</c> — callers that need the full
/// unpaged list (e.g. the product BOM editor) rely on that. When <see cref="Page"/> is set,
/// <c>Skip</c>/<c>Take</c> are applied after filtering and ordering, and
/// <see cref="GetComponentsResponse.TotalCount"/> describes the unpaged result set.
/// Out-of-range <see cref="PageSize"/> is clamped, never rejected.
/// </remarks>
public class GetComponentsQuery : IRequest<GetComponentsResponse>
{
    /// <summary>
    /// Optional status filter. When omitted, components of both statuses are returned and the
    /// caller is responsible for filtering (per-status counts are returned when
    /// <see cref="IncludeCounts"/> is set).
    /// </summary>
    public bool? IsActive { get; set; }

    /// <summary>
    /// Optional free-text filter over the component name: case-insensitive substring match.
    /// Blank or whitespace-only values are treated as "no search". The value is trimmed and
    /// capped at 200 characters.
    /// </summary>
    public string? Search { get; set; }

    /// <summary>
    /// Optional 1-based page number. Null means "no pagination": the full list is returned.
    /// Values below 1 are treated as page 1.
    /// </summary>
    public int? Page { get; set; }

    /// <summary>
    /// Page size, clamped to 1..200. Only applied when <see cref="Page"/> is set.
    /// </summary>
    public int? PageSize { get; set; }

    /// <summary>
    /// Sort key, resolved against a whitelist: name, supplier, validFrom, createdAt.
    /// Unknown or missing values fall back to name.
    /// </summary>
    public string? SortBy { get; set; }

    /// <summary>
    /// Sort direction: "asc" (default) or "desc". Any other value falls back to ascending.
    /// </summary>
    public string? SortDir { get; set; }

    /// <summary>
    /// When true, <see cref="GetComponentsResponse.StatusCounts"/> is populated with per-status
    /// totals computed over the whole (unpaged) result set, so tab counters stay correct
    /// while the list itself is paged. The same search filter is applied, so the counters
    /// describe the current search results rather than the whole catalog.
    /// </summary>
    public bool IncludeCounts { get; set; }
}

public class GetComponentsResponse
{
    /// <summary>
    /// Rows of the requested page, or the full list when pagination was not requested.
    /// </summary>
    public IReadOnlyList<ComponentItem> Items { get; set; } = new List<ComponentItem>();

    /// <summary>
    /// Total number of components matching the search filter, ignoring pagination.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Page that was actually served (1-based). 1 when pagination was not requested.
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// Page size that was actually applied, after clamping. Equal to <see cref="TotalCount"/>
    /// when pagination was not requested.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Per-status totals, present only when the request asked for counts. Lets the client
    /// render tab counters for a paged list in a single round trip.
    /// </summary>
    public ComponentStatusCounts? StatusCounts { get; set; }
}

/// <summary>
/// Per-status component totals, independent of the active status filter.
/// </summary>
public class ComponentStatusCounts
{
    public int Active { get; set; }

    public int Inactive { get; set; }

    /// <summary>Total across both statuses.</summary>
    public int All { get; set; }
}

public class ComponentItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Domain.Enums.Unit Unit { get; set; }

    /// <summary>Категория компонента, определяющая статью себестоимости.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;

    /// <summary>Currently active price version, or null when the component has no active price.</summary>
    public ComponentPriceDto? ActivePrice { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

public class GetComponentsQueryHandler : IRequestHandler<GetComponentsQuery, GetComponentsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MinPageSize = 1;

    private readonly IComponentRepository _repository;

    public GetComponentsQueryHandler(IComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetComponentsResponse> Handle(GetComponentsQuery request, CancellationToken cancellationToken)
    {
        // No page requested means "give me everything" — the legacy contract that the product
        // BOM editor still relies on. Paging is applied only when a page number arrives.
        int page = Math.Max(request.Page ?? 1, 1);
        int pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        int? skip = request.Page.HasValue ? (page - 1) * pageSize : null;
        int? take = request.Page.HasValue ? pageSize : null;

        bool descending = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        (int totalCount, IReadOnlyList<Component> components) = await _repository.GetPagedAsync(
            request.IsActive,
            skip,
            take,
            request.SortBy,
            descending,
            cancellationToken,
            request.Search);

        GetComponentsResponse response = new()
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = components.Select(c => new ComponentItem
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Unit = c.Unit,
                Category = c.Category,
                ActivePrice = ComponentPriceMapper.Map(c.Prices.FirstOrDefault(p => p.IsActive)),
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
            }).ToList(),
        };

        if (request.IncludeCounts)
        {
            (int active, int inactive, int all) = await _repository.GetStatusCountsAsync(cancellationToken, request.Search);
            response.StatusCounts = new ComponentStatusCounts
            {
                Active = active,
                Inactive = inactive,
                All = all,
            };
        }

        return response;
    }
}
