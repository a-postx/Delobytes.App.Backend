using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

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

public class UpdateProductResponse
{
    public bool Found { get; set; }
}

/// <summary>
/// Applies a partial update to a product.
///
/// The handler treats every null field as "not sent": it is skipped entirely, so a caller that
/// only changes the SKU cannot accidentally blank the name, drop barcodes, or deactivate the
/// packing unit. Only fields the caller actually supplied are written.
///
/// SKU changes are safe for marketplace-linked products. The import matches cards by nmID and by
/// barcode and never consults Product.Sku (see ImportProductBatchConsumer.FindProductByBarcodeAsync),
/// so renaming the internal SKU cannot re-route or duplicate a later import. The channel's own
/// vendor code is kept separately on ChannelProduct.ExternalSku and continues to be refreshed on
/// every sync.
/// </summary>
public class UpdateProductCommandHandler : IRequestHandler<UpdateProductCommand, UpdateProductResponse>
{
    private readonly IProductRepository _repository;

    public UpdateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateProductResponse> Handle(UpdateProductCommand request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (product == null || product.Status == ProductStatus.Deleted)
        {
            return new UpdateProductResponse { Found = false };
        }

        if (request.Sku != null)
        {
            product.Sku = request.Sku.Trim();
        }

        if (request.Name != null)
        {
            product.Name = request.Name.Trim();
        }

        if (request.Description != null)
        {
            // An empty string is a value, not an omission: it is how the frontend clears the field.
            string trimmedDescription = request.Description.Trim();
            product.Description = trimmedDescription.Length == 0 ? null : trimmedDescription;
        }

        if (request.Barcodes != null)
        {
            ApplyBarcodes(product, request.Barcodes);
        }

        if (request.PackingUnit != null)
        {
            ApplyPackingUnit(product, request.PackingUnit);
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProductResponse { Found = true };
    }

    /// <summary>
    /// Replaces the barcode set with the one supplied. Barcodes are sent as a complete collection
    /// by the caller, so an id present in the request is updated, an entry without an id is added,
    /// and any stored barcode not mentioned in the request is removed.
    /// </summary>
    private static void ApplyBarcodes(Product product, List<ProductBarcodeDto> barcodes)
    {
        List<Guid> incomingIds = barcodes
            .Where(dto => dto.Id.HasValue)
            .Select(dto => dto.Id!.Value)
            .ToList();

        List<ProductBarcode> toRemove = product.Barcodes
            .Where(b => !incomingIds.Contains(b.Id))
            .ToList();

        foreach (ProductBarcode barcode in toRemove)
        {
            product.Barcodes.Remove(barcode);
        }

        foreach (ProductBarcodeDto dto in barcodes)
        {
            if (dto.Id.HasValue)
            {
                ProductBarcode? existing = product.Barcodes.FirstOrDefault(b => b.Id == dto.Id.Value);

                if (existing != null)
                {
                    existing.Value = dto.Value;
                    existing.Type = dto.Type;
                    existing.IsDefault = dto.IsDefault;
                }
            }
            else
            {
                product.Barcodes.Add(new ProductBarcode
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Value = dto.Value,
                    Type = dto.Type,
                    IsDefault = dto.IsDefault
                });
            }
        }
    }

    /// <summary>
    /// Writes dimensions onto the active packing unit, creating one when the product has none.
    /// </summary>
    private static void ApplyPackingUnit(Product product, PackingUnitDto packingUnit)
    {
        PackingUnit? existing = product.PackingUnits.FirstOrDefault(pu => pu.IsActive);

        if (existing == null)
        {
            product.PackingUnits.Add(new PackingUnit
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                LengthCm = packingUnit.LengthCm,
                WidthCm = packingUnit.WidthCm,
                HeightCm = packingUnit.HeightCm,
                WeightKg = packingUnit.WeightKg,
                IsActive = true
            });

            return;
        }

        existing.LengthCm = packingUnit.LengthCm;
        existing.WidthCm = packingUnit.WidthCm;
        existing.HeightCm = packingUnit.HeightCm;
        existing.WeightKg = packingUnit.WeightKg;
    }
}
