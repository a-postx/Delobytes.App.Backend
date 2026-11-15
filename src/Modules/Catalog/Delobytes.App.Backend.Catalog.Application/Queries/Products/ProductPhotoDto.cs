namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// A photo of a product's own gallery. Channel galleries are exposed through
/// <c>ProductChannelLinkDto.Photos</c> instead.
/// </summary>
public class ProductPhotoDto
{
    public Guid Id { get; set; }

    public int DisplayOrder { get; set; }

    public string SizeVariant { get; set; } = default!;

    public string Url { get; set; } = default!;

    public int? Width { get; set; }

    public int? Height { get; set; }
}
