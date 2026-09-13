using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;

public class GetComponentsResponse
{
    public IReadOnlyList<ComponentItem> Items { get; set; } = new List<ComponentItem>();
}

public class ComponentItem
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Unit Unit { get; set; }

    /// <summary>Currently active price version, or null when the component has no active price.</summary>
    public ComponentPriceDto? ActivePrice { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
