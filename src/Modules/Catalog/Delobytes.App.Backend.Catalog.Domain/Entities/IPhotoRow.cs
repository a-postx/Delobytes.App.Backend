using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Domain.Entities;

/// <summary>
/// The fields shared by the two photo rows the catalogue stores: a canonical
/// <see cref="ProductPhoto"/> and a channel-owned <see cref="ChannelProductPhoto"/>.
/// Exists so the download/hash/upload pipeline is written once; it is not a navigation contract.
/// </summary>
public interface IPhotoRow
{
    /// <summary>Gets or sets the unique identifier.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the position among the owner's photos, 1-based; shared by both size variants.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Gets or sets the size variant, "thumbnail" or "large".</summary>
    public string SizeVariant { get; set; }

    /// <summary>Gets or sets the slot key within the owning set, "{DisplayOrder}_{SizeVariant}".</summary>
    public string ExternalId { get; set; }

    /// <summary>Gets or sets the object key; content-addressed so identical bytes share one blob.</summary>
    public string StorageKey { get; set; }

    /// <summary>Gets or sets the SHA-256 of the stored bytes, lowercase hex.</summary>
    public string? ContentHash { get; set; }

    /// <summary>Gets or sets the stored content type.</summary>
    public string ContentType { get; set; }

    /// <summary>Gets or sets the stored size in bytes.</summary>
    public long SizeBytes { get; set; }

    /// <summary>Gets or sets the rendered width in pixels, when known.</summary>
    public int? Width { get; set; }

    /// <summary>Gets or sets the rendered height in pixels, when known.</summary>
    public int? Height { get; set; }

    /// <summary>Gets or sets the source marketplace name.</summary>
    public string Source { get; set; }

    /// <summary>Gets or sets the lifecycle state.</summary>
    public ProductPhotoStatus Status { get; set; }

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
}
