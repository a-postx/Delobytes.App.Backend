namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

public class ProductPhotoDto
{
    public Guid Id { get; set; }

    public int DisplayOrder { get; set; }

    public string SizeVariant { get; set; } = default!;

    public string Url { get; set; } = default!;

    public int? Width { get; set; }

    public int? Height { get; set; }
}
