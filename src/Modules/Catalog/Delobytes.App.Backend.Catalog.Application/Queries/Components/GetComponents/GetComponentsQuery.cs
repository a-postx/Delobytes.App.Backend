using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;

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
}
