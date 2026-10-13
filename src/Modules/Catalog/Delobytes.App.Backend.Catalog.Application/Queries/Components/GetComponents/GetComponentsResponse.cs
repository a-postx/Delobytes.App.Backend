using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;

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
}

public class ComponentItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Unit Unit { get; set; }

    /// <summary>Категория компонента, определяющая статью себестоимости.</summary>
    public ComponentCategory Category { get; set; } = ComponentCategory.Material;

    /// <summary>Currently active price version, or null when the component has no active price.</summary>
    public ComponentPriceDto? ActivePrice { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
