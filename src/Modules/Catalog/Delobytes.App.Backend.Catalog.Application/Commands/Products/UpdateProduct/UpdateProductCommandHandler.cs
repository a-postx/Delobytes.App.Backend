using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.UpdateProduct;

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

        product.Name = request.Name;
        product.Description = request.Description;

        if (request.Barcodes != null)
        {
            List<Guid> incomingIds = request.Barcodes
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

            foreach (ProductBarcodeDto dto in request.Barcodes)
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

        if (request.PackingUnit != null)
        {
            PackingUnit? existing = product.PackingUnits.FirstOrDefault(pu => pu.IsActive);

            if (existing == null)
            {
                product.PackingUnits.Add(new PackingUnit
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    LengthCm = request.PackingUnit.LengthCm,
                    WidthCm = request.PackingUnit.WidthCm,
                    HeightCm = request.PackingUnit.HeightCm,
                    WeightKg = request.PackingUnit.WeightKg,
                    IsActive = true
                });
            }
            else
            {
                existing.LengthCm = request.PackingUnit.LengthCm;
                existing.WidthCm = request.PackingUnit.WidthCm;
                existing.HeightCm = request.PackingUnit.HeightCm;
                existing.WeightKg = request.PackingUnit.WeightKg;
            }
        }

        await _repository.SaveChangesAsync(cancellationToken);
        return new UpdateProductResponse { Found = true };
    }
}
