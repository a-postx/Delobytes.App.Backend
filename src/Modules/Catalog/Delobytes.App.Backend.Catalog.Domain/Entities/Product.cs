using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// Represents a product in the catalog.
/// </summary>
public class Product : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    /// <summary>
    /// Immutable code assigned from the default numbering scheme when the product is created.
    /// Never part of a create or update payload.
    /// </summary>
    public string Code { get; set; } = default!;

    /// <summary>Internal SKU code.</summary>
    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public ProductStatus Status { get; private set; } = ProductStatus.Active;

    /// <summary>
    /// Gets or sets the source from which this product was created.
    /// </summary>
    public CreationSource CreationSource { get; set; } = CreationSource.Manual;

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public DateTimeOffset? ArchivedAt { get; private set; }

    public uint RowVersion { get; set; }

    /// <summary>Set when deletion is requested; cleared on restore or completion.</summary>
    public DateTimeOffset? DeletionRequestedAt { get; private set; }

    public DateTimeOffset? DeletedAt { get; private set; }

    public ICollection<ChannelProduct> ChannelProducts { get; set; } = new List<ChannelProduct>();

    public ICollection<BomLine> BomLines { get; set; } = new List<BomLine>();

    public ICollection<ProductWorkRate> ProductWorkRates { get; set; } = new List<ProductWorkRate>();

    public ICollection<PackingUnit> PackingUnits { get; set; } = new List<PackingUnit>();

    public ICollection<ProductBarcode> Barcodes { get; set; } = new List<ProductBarcode>();

    public ICollection<ProductPhoto> Photos { get; set; } = new List<ProductPhoto>();

    /// <summary>
    /// Moves the product to <see cref="ProductStatus.Archived"/>. A repeated archive is a no-op and
    /// keeps the original <see cref="ArchivedAt"/>; an archived product leaves any pending deletion
    /// check behind, because it is no longer waiting to be deleted.
    /// </summary>
    /// <param name="now">Time of the archive.</param>
    /// <returns><see langword="true"/> when the state changed.</returns>
    public bool TryArchive(DateTimeOffset now)
    {
        if (Status == ProductStatus.Deleted || Status == ProductStatus.Archived)
        {
            return false;
        }

        Status = ProductStatus.Archived;
        ArchivedAt = now;
        DeletionRequestedAt = null;
        return true;
    }

    /// <summary>
    /// Returns an archived product, or one whose deletion was cancelled, to the active catalogue.
    /// </summary>
    /// <returns><see langword="true"/> when the state changed.</returns>
    public bool TryRestore()
    {
        if (Status != ProductStatus.Archived && Status != ProductStatus.DeletionFailed)
        {
            return false;
        }

        Status = ProductStatus.Active;
        ArchivedAt = null;
        DeletionRequestedAt = null;
        return true;
    }

    /// <summary>
    /// Marks the product as waiting for the Sales-side orders check.
    /// </summary>
    /// <param name="now">Time of the request.</param>
    /// <returns><see langword="true"/> when the state changed.</returns>
    public bool TryRequestDeletion(DateTimeOffset now)
    {
        if (Status == ProductStatus.Deleted || Status == ProductStatus.DeletionPending)
        {
            return false;
        }

        Status = ProductStatus.DeletionPending;
        DeletionRequestedAt = now;
        return true;
    }

    /// <summary>
    /// Soft-deletes the product. <see cref="DeletionRequestedAt"/> is kept as the audit trail of the
    /// request that led here.
    /// </summary>
    /// <param name="now">Time of the deletion.</param>
    /// <returns><see langword="true"/> when the state changed.</returns>
    public bool TryCompleteDeletion(DateTimeOffset now)
    {
        if (Status == ProductStatus.Deleted)
        {
            return false;
        }

        Status = ProductStatus.Deleted;
        DeletedAt = now;
        return true;
    }

    /// <summary>
    /// Cancels a pending deletion because the product still has orders.
    /// </summary>
    /// <returns><see langword="true"/> when the state changed.</returns>
    public bool TryFailDeletion()
    {
        if (Status != ProductStatus.DeletionPending)
        {
            return false;
        }

        Status = ProductStatus.DeletionFailed;
        DeletionRequestedAt = null;
        return true;
    }
}
