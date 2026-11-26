using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Immutable price version for a component.
/// Changing price or supplier creates a new record with a new ValidFrom;
/// existing records are never modified so historical cost calculations remain intact.
/// </summary>
public class ComponentPrice : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    public Guid ComponentId { get; set; }

    /// <summary>
    /// Price in currency for <see cref="Quantity"/> units of the component, not for a single unit:
    /// a purchase of 150 pieces for 100 leaves both figures as they were entered.
    /// </summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>
    /// Number of component units (in the component's own unit) the price refers to. One unit keeps
    /// the historical meaning of <see cref="PricePerUnit"/>; the cost of a single unit is
    /// <c>PricePerUnit / Quantity</c>.
    /// </summary>
    public decimal Quantity { get; set; } = 1m;

    public Guid? SupplierId { get; set; }

    /// <summary>Date from which this price version becomes effective.</summary>
    public DateOnly ValidFrom { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Set only on deactivation/restore; never changes ValidFrom.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }

    public Component Component { get; set; } = default!;

    public Supplier? Supplier { get; set; }
}
