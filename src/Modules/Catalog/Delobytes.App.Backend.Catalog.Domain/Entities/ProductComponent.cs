using Delobytes.App.Backend.Identity.Domain.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents the many-to-many relationship between Product and Component with quantity.
/// </summary>
public class ProductComponent : ITenantScoped
{
    /// <summary>
    /// Gets or sets the product component unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the component identifier.
    /// </summary>
    public Guid ComponentId { get; set; }

    /// <summary>
    /// Gets or sets the quantity of the component used in the product.
    /// </summary>
    public decimal Quantity { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the product component was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Navigation property: the product.
    /// </summary>
    public Product Product { get; set; } = default!;

    /// <summary>
    /// Navigation property: the component.
    /// </summary>
    public Component Component { get; set; } = default!;
}
