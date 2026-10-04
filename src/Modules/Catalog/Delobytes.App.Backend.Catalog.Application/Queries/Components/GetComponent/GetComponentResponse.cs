using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponent;

public class GetComponentResponse
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
