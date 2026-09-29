using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// A single size variant of a product photo stored in Object Storage.
/// One marketplace photo yields two rows: "thumbnail" and "large".
/// </summary>
public class ProductPhoto : ITenantScoped, IAuditableEntity, IRowVersionedEntity
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    /// <summary>Position among the product's photos, 1-based; shared by both size variants.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>"thumbnail" or "large".</summary>
    public string SizeVariant { get; set; } = default!;

    /// <summary>Object key: product-photos/{tenantId:N}/{productId:N}/{photoId:N}.webp</summary>
    public string StorageKey { get; set; } = default!;

    public string OriginalFileName { get; set; } = default!;

    public string ContentType { get; set; } = default!;

    public long SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public ProductPhotoStatus Status { get; set; } = ProductPhotoStatus.Pending;

    /// <summary>Source marketplace, e.g. "Wildberries".</summary>
    public string Source { get; set; } = default!;

    /// <summary>"{nmId}_{displayOrder}_{sizeVariant}" — the idempotency key across re-imports.</summary>
    public string ExternalId { get; set; } = default!;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }

    public Product Product { get; set; } = default!;
}
