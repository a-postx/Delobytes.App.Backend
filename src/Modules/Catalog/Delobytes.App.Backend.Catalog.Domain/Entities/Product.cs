using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a product in the catalog.
/// </summary>
public class Product : ITenantScoped
{
    /// <summary>
    /// Gets or sets the product unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the internal SKU (stock keeping unit).
    /// </summary>
    public string Sku { get; set; } = default!;

    /// <summary>
    /// Gets or sets the product name.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the product description.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the product is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the product was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the product was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: channel products (product linked to channels).
    /// </summary>
    public ICollection<ChannelProduct> ChannelProducts { get; set; } = new List<ChannelProduct>();

    /// <summary>
    /// Navigation property: product components (materials/components used).
    /// </summary>
    public ICollection<ProductComponent> ProductComponents { get; set; } = new List<ProductComponent>();

    /// <summary>
    /// Navigation property: product labor costs.
    /// </summary>
    public ICollection<ProductLaborCost> ProductLaborCosts { get; set; } = new List<ProductLaborCost>();
}
