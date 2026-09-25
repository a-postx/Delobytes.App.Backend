namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// Barcode of a product as exposed through the API.
/// Shared by the product list and single-product queries; a separate copy per
/// query namespace would collide in the generated OpenAPI document, because
/// Swashbuckle derives schema ids from the short type name.
/// </summary>
public class ProductBarcodeDto
{
    public Guid? Id { get; set; }

    public string Value { get; set; } = default!;

    /// <summary>Barcode system or source, e.g. "EAN13", "WB", "Ozon".</summary>
    public string? Type { get; set; }

    public bool IsDefault { get; set; }
}
