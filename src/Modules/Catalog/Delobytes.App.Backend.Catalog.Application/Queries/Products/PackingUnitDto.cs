namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// Dimensions and weight of one packed product unit as exposed through the API.
/// Shared by the product list and single-product queries — see ProductBarcodeDto
/// for why a single declaration is required.
/// </summary>
public class PackingUnitDto
{
    public decimal LengthCm { get; set; }

    public decimal WidthCm { get; set; }

    public decimal HeightCm { get; set; }

    public decimal? WeightKg { get; set; }
}
