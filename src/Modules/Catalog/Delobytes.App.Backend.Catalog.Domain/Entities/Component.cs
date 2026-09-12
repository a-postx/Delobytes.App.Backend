using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a component or material used in product manufacturing.
/// </summary>
public class Component : ITenantScoped
{
    /// <summary>
    /// Gets or sets the component unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the component name.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the unit of measurement for the component.
    /// </summary>
    public Unit Unit { get; set; }

    /// <summary>
    /// Gets or sets the purchase price per unit.
    /// </summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>
    /// Gets or sets the supplier name.
    /// </summary>
    public string? Supplier { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the component is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the component was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the component was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: products using this component.
    /// </summary>
    public ICollection<ProductComponent> ProductComponents { get; set; } = new List<ProductComponent>();
}
