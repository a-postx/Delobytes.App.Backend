using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Immutable price version for a component.
/// Changing price or supplier creates a new record with a new ValidFrom;
/// existing records are never modified so historical cost calculations remain intact.
/// </summary>
public class ComponentPrice : ITenantScoped
{
    public Guid Id { get; set; }

    public Guid ComponentId { get; set; }

    /// <summary>Purchase price per unit in currency.</summary>
    public decimal PricePerUnit { get; set; }

    public Guid? SupplierId { get; set; }

    /// <summary>Date from which this price version becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set only on deactivation/restore; never changes ValidFrom.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public Component Component { get; set; } = default!;

    public Supplier? Supplier { get; set; }
}
