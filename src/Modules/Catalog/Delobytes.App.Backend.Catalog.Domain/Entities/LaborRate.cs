using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a labor rate for calculating product labor costs.
/// </summary>
public class LaborRate : ITenantScoped
{
    /// <summary>
    /// Gets or sets the labor rate unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the labor rate name.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// Gets or sets the rate amount.
    /// </summary>
    public decimal Rate { get; set; }

    /// <summary>
    /// Gets or sets the rate period (daily or monthly).
    /// </summary>
    public RatePeriod RatePeriod { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the rate includes taxes.
    /// </summary>
    public bool IncludesTaxes { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the labor rate is active.
    /// </summary>
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the labor rate was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the labor rate was last updated.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property: products using this labor rate.
    /// </summary>
    public ICollection<ProductLaborCost> ProductLaborCosts { get; set; } = new List<ProductLaborCost>();
}
