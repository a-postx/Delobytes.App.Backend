using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Versioned purchase cost of raw material for a specific product.
/// Adding a new record creates a new version; previous records are never modified,
/// preserving accuracy of historical cost calculations.
/// The active record for a given calculation date is the one with the greatest ValidFrom that is still &lt;= that date.
/// </summary>
public class RawMaterialRate : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Purchase cost of raw material per one product unit, in currency.</summary>
    public decimal CostPerUnit { get; set; }

    /// <summary>Date from which this rate version becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Product Product { get; set; } = default!;
}
