using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// A single size variant of a photo the marketplace reports for one of its cards.
/// One marketplace photo yields two rows: "thumbnail" and "large".
/// </summary>
/// <remarks>
/// Channel-owned data: the import replaces the card's whole set on every run, and these rows are
/// never promoted to <see cref="Product.Photos"/> once the product exists. The split is deliberate:
/// a product imported from two channels keeps each channel's gallery apart, and a re-import cannot
/// add photos to the catalogue's own set.
/// </remarks>
public class ChannelProductPhoto : ITenantScoped, IRowVersionedEntity, IPhotoRow
{
    /// <summary>Gets or sets the unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the link of the card this photo belongs to.</summary>
    public Guid ChannelProductId { get; set; }

    /// <summary>Gets or sets the slot key within the card, "{DisplayOrder}_{SizeVariant}".</summary>
    public string ExternalId { get; set; } = default!;

    /// <summary>Gets or sets the position among the card's photos, 1-based.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Gets or sets "thumbnail" or "large".</summary>
    public string SizeVariant { get; set; } = default!;

    /// <summary>Gets or sets the object key; content-addressed, shared with the canonical set.</summary>
    public string StorageKey { get; set; } = default!;

    /// <summary>Gets or sets the SHA-256 of the stored bytes, lowercase hex.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Gets or sets the stored content type.</summary>
    public string ContentType { get; set; } = default!;

    /// <summary>Gets or sets the stored size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the rendered width in pixels, when known.</summary>
    public int? Width { get; set; }

    /// <summary>Gets or sets the rendered height in pixels, when known.</summary>
    public int? Height { get; set; }

    /// <summary>Gets or sets the source marketplace name.</summary>
    public string Source { get; set; } = default!;

    /// <summary>Gets or sets the lifecycle state.</summary>
    public ProductPhotoStatus Status { get; set; } = ProductPhotoStatus.Pending;

    /// <summary>Gets or sets the last failure reason, when the row failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets when the row was last checked against the marketplace.</summary>
    public DateTimeOffset? LastCheckedAt { get; set; }

    /// <summary>Gets or sets when the row was superseded; the blob is retained during the grace period.</summary>
    public DateTimeOffset? RemovedAt { get; set; }

    /// <summary>Gets or sets when the row was first written.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets when the row was last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Gets or sets the user who last changed the row, when the change was user-initiated.</summary>
    public Guid? UpdatedByUserId { get; set; }

    /// <summary>Gets or sets the optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint RowVersion { get; set; }

    /// <summary>Navigation property: the card the photo belongs to.</summary>
    public ChannelProduct ChannelProduct { get; set; } = default!;
}
