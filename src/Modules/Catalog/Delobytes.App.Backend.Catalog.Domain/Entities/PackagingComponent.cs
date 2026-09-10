using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a packaging material or component (e.g. box, bubble wrap, tape).
/// Price changes create a new record rather than updating the existing one
/// so that historical cost calculations remain intact.
/// </summary>
public class PackagingComponent : ITenantScoped
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public Unit Unit { get; set; }

    /// <summary>Purchase price per unit in currency.</summary>
    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    public Supplier? Supplier { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public ICollection<ProductPackagingComponent> ProductPackagingComponents { get; set; } = new List<ProductPackagingComponent>();
}
