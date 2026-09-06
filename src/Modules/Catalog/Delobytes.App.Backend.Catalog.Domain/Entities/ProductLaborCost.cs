using Delobytes.App.Backend.Identity.Domain.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents the many-to-many relationship between Product and LaborRate with production units.
/// </summary>
public class ProductLaborCost : ITenantScoped
{
    /// <summary>
    /// Gets or sets the product labor cost unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the product identifier.
    /// </summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the labor rate identifier.
    /// </summary>
    public Guid LaborRateId { get; set; }

    /// <summary>
    /// Gets or sets the number of units produced per period.
    /// </summary>
    public int UnitsProducedPerPeriod { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the product labor cost was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Navigation property: the product.
    /// </summary>
    public Product Product { get; set; } = default!;

    /// <summary>
    /// Navigation property: the labor rate.
    /// </summary>
    public LaborRate LaborRate { get; set; } = default!;
}
