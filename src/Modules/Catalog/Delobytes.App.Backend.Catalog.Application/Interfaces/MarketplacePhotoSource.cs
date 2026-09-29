namespace Delobytes.App.Backend.Catalog.Application.Interfaces;

/// <summary>
/// A single photo variant as reported by a marketplace before it is downloaded.
/// </summary>
public record MarketplacePhotoSource
{
    /// <summary>Absolute URL the bytes are downloaded from.</summary>
    public required string Url { get; init; }

    /// <summary>
    /// Idempotency key across re-imports. Expected form: "{nmId}_{displayOrder}_{sizeVariant}".
    /// </summary>
    public required string ExternalId { get; init; }

    /// <summary>Position among the product's photos, 1-based.</summary>
    public required int DisplayOrder { get; init; }

    /// <summary>"thumbnail" or "large".</summary>
    public required string SizeVariant { get; init; }

    /// <summary>Source width in pixels, when reported by the marketplace.</summary>
    public int? Width { get; init; }

    /// <summary>Source height in pixels, when reported by the marketplace.</summary>
    public int? Height { get; init; }

    /// <summary>
    /// Marketplace that produced this photo. Defaults to the only source implemented so far,
    /// but stays per-source so a second marketplace does not require a schema or contract change.
    /// </summary>
    public string Source { get; init; } = MarketplacePhotoSources.Wildberries;
}

/// <summary>
/// Known marketplace names used as the <c>ProductPhoto.Source</c> value.
/// Free text on purpose: a new marketplace must not require a migration.
/// </summary>
public static class MarketplacePhotoSources
{
    /// <summary>Wildberries marketplace.</summary>
    public const string Wildberries = "Wildberries";
}
