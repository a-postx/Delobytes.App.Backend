namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.RestoreProduct;

public class RestoreProductResponse
{
    public bool Found { get; set; }

    /// <summary>False when the product status does not allow restore.</summary>
    public bool Accepted { get; set; }
}
