using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a product in the catalog.
/// </summary>
public class Product : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    /// <summary>Internal SKU code.</summary>
    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public ProductStatus Status { get; set; } = ProductStatus.Active;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public uint RowVersion { get; set; }

    /// <summary>Set when deletion is requested; cleared on restore or completion.</summary>
    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    public ICollection<ChannelProduct> ChannelProducts { get; set; } = new List<ChannelProduct>();

    public ICollection<ProductComponent> ProductComponents { get; set; } = new List<ProductComponent>();

    public ICollection<ProductChannelInput> ProductChannelInputs { get; set; } = new List<ProductChannelInput>();

    public ICollection<ProductWorkRate> ProductWorkRates { get; set; } = new List<ProductWorkRate>();

    public ICollection<PackingUnit> PackingUnits { get; set; } = new List<PackingUnit>();

    public ICollection<ProductBarcode> Barcodes { get; set; } = new List<ProductBarcode>();
}
