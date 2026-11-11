namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Channel-neutral snapshot of one product card, published by an import run.
/// </summary>
public record ChannelProductCard
{
    /// <summary>
    /// The channel's identifier of the product, as a string because channels do not agree on a
    /// numeric identifier.
    /// </summary>
    public string ExternalProductId { get; init; } = default!;

    /// <summary>
    /// The seller's article for the product (offer id / vendor code), when the channel reports one.
    /// </summary>
    public string? OfferId { get; init; }

    /// <summary>
    /// Product name.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Product description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Barcodes reported for the product.
    /// </summary>
    public List<string> Barcodes { get; init; } = new();

    /// <summary>
    /// Channel whose barcodes these are, so Catalog can label them without knowing the channel.
    /// </summary>
    public string? BarcodeType { get; init; }

    /// <summary>
    /// Outer package length in cm, as reported by the marketplace.
    /// </summary>
    public decimal? LengthCm { get; init; }

    /// <summary>
    /// Outer package width in cm, as reported by the marketplace.
    /// </summary>
    public decimal? WidthCm { get; init; }

    /// <summary>
    /// Outer package height in cm, as reported by the marketplace.
    /// </summary>
    public decimal? HeightCm { get; init; }

    /// <summary>
    /// Gross weight of the packed unit in kg, as reported by the marketplace.
    /// </summary>
    public decimal? WeightKg { get; init; }

    /// <summary>
    /// Channel-specific payload serialized as JSON, kept verbatim for diagnostics and future use.
    /// </summary>
    public string? ChannelSpecificData { get; init; }

    /// <summary>
    /// Code of the channel the card was read from.
    /// </summary>
    public string ChannelCode { get; init; } = default!;

    /// <summary>
    /// Photo variants of the card, in the order the marketplace reports them.
    /// </summary>
    public List<ChannelProductPhoto> Photos { get; init; } = new();
}

/// <summary>
/// One downloadable photo variant of a product card.
/// </summary>
public record ChannelProductPhoto
{
    /// <summary>
    /// Absolute URL of the image.
    /// </summary>
    public string Url { get; init; } = default!;

    /// <summary>
    /// Variant name the importer stores ("thumbnail", "large", ...), chosen by the channel mapper.
    /// </summary>
    public string SizeVariant { get; init; } = default!;

    /// <summary>
    /// Image width in pixels, when the channel reports it.
    /// </summary>
    public int? Width { get; init; }

    /// <summary>
    /// Image height in pixels, when the channel reports it.
    /// </summary>
    public int? Height { get; init; }

    /// <summary>
    /// Position of the photo in the card's gallery, starting at 1.
    /// </summary>
    public int DisplayOrder { get; init; }
}
