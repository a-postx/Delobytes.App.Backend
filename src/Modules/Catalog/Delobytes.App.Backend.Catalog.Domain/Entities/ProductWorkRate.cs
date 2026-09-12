using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Versioned assembly output rate for a specific product.
/// Captures how many units of this product one worker produces per day.
/// Adding a new record creates a new version; previous records are never modified,
/// preserving accuracy of historical cost calculations.
/// </summary>
public class ProductWorkRate : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Number of finished units one worker assembles per day for this product.</summary>
    public int AssemblyRatePerDay { get; set; }

    /// <summary>Date from which this rate version becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>False when the record is soft-deleted; historical snapshots remain intact.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Product Product { get; set; } = default!;
}
