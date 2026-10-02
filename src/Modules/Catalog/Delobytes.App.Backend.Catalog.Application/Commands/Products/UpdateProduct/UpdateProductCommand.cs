using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;

/// <summary>
/// Partial update of a product.
///
/// Every property is optional, and "absent" is not the same as "empty":
/// <list type="bullet">
///   <item><description><see langword="null"/> — the field is left untouched.</description></item>
///   <item><description>a value — the field is written.</description></item>
/// </list>
///
/// <see cref="Name"/> and <see cref="Sku"/> therefore cannot be explicitly cleared: an empty
/// string is rejected by the validator, and a null skips the field. That is intentional — the
/// catalog has no concept of a nameless or SKU-less product.
///
/// <see cref="Barcodes"/> keeps the pre-existing convention instead: null and an empty list are
/// both read as "the user cleared all barcodes", because the frontend sends the full barcode set
/// on every save of a manual product. Tightening that contract belongs with the write-back work,
/// not with allowing the SKU to be edited.
/// </summary>
public class UpdateProductCommand : IRequest<UpdateProductResponse>
{
    public Guid Id { get; set; }

    /// <summary>Gets or sets the new SKU. Null leaves the current SKU in place.</summary>
    public string? Sku { get; set; }

    /// <summary>Gets or sets the new name. Null leaves the current name in place.</summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the new description. Null leaves the current description in place; an empty
    /// or whitespace-only string clears it, which is how the frontend sends "user emptied the
    /// field".
    /// </summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets the full barcode set. Null means "clear all barcodes".</summary>
    public List<ProductBarcodeDto>? Barcodes { get; set; }

    /// <summary>Gets or sets the active packing unit. Null leaves it in place.</summary>
    public PackingUnitDto? PackingUnit { get; set; }
}
