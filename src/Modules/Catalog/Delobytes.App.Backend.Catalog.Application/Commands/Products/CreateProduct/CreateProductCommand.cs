using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.CreateProduct;

public class CreateProductCommand : IRequest<CreateProductResponse>
{
    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public List<ProductBarcodeDto>? Barcodes { get; set; }

    public PackingUnitDto? PackingUnit { get; set; }
}
