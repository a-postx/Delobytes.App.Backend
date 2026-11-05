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

    /// <summary>
    /// Channel that declared this photo.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>Position among the product's photos, 1-based; shared by both size variants.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>"thumbnail" or "large".</summary>
    public string SizeVariant { get; set; } = default!;

    /// <summary>Object key, content-addressed: product-photos/{tenantId:N}/{productId:N}/{contentHash}.webp</summary>
    public string StorageKey { get; set; } = default!;

    /// <summary>
    /// SHA-256 of the stored bytes, lowercase hex.
    /// </summary>
    public string? ContentHash { get; set; }

    public string OriginalFileName { get; set; } = default!;

    public string ContentType { get; set; } = default!;

    public long SizeBytes { get; set; }

    public int? Width { get; set; }

    public int? Height { get; set; }

    public ProductPhotoStatus Status { get; set; } = ProductPhotoStatus.Pending;

    /// <summary>Source marketplace, e.g. "Wildberries".</summary>
    public string Source { get; set; } = default!;

    /// <summary>
    /// Slot key within the channel, "{nmId}_{displayOrder}_{sizeVariant}"; no longer globally unique.
    /// </summary>
    public string ExternalId { get; set; } = default!;

    public string? ErrorMessage { get; set; }

    public DateTimeOffset? LastCheckedAt { get; set; }

    public DateTimeOffset? RemovedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public Guid? UpdatedByUserId { get; set; }

    public uint RowVersion { get; set; }

    public Product Product { get; set; } = default!;
}
