namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// A photo of one channel card's own gallery.
/// </summary>
public class ProductChannelPhotoDto
{
    /// <summary>Gets or sets the position among the card's photos, 1-based.</summary>
    public int DisplayOrder { get; set; }

    /// <summary>Gets or sets "thumbnail" or "large".</summary>
    public string SizeVariant { get; set; } = default!;

    /// <summary>Gets or sets the publicly reachable URL of the stored blob.</summary>
    public string Url { get; set; } = default!;
}
