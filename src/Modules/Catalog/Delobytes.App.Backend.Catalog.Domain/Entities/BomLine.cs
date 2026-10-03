using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Versioned BOM line linking a product to a component. New records create composition versions;
/// superseded records are deactivated so historical calculations remain reproducible.
/// </summary>
public class BomLine : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly ValidFrom { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public uint RowVersion { get; set; }
    public Product Product { get; set; } = default!;
    public Component Component { get; set; } = default!;
}
