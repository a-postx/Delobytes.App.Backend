using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products.CreateProduct;

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, CreateProductResponse>
{
    private readonly IProductRepository _repository;

    public CreateProductCommandHandler(IProductRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateProductResponse> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        Product product = new Product
        {
            Id = Guid.NewGuid(),
            Sku = request.Sku,
            Name = request.Name,
            Description = request.Description,
            Status = ProductStatus.Active,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        if (request.Barcodes != null)
        {
            foreach (ProductBarcodeDto dto in request.Barcodes)
            {
                product.Barcodes.Add(new ProductBarcode
                {
                    Id = Guid.NewGuid(),
                    ProductId = product.Id,
                    Value = dto.Value,
                    Type = dto.Type,
                    IsDefault = dto.IsDefault,
                    CreatedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        if (request.PackingUnit != null)
        {
            product.PackingUnits.Add(new PackingUnit
            {
                Id = Guid.NewGuid(),
                ProductId = product.Id,
                LengthCm = request.PackingUnit.LengthCm,
                WidthCm = request.PackingUnit.WidthCm,
                HeightCm = request.PackingUnit.HeightCm,
                WeightKg = request.PackingUnit.WeightKg,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        _repository.Add(product);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateProductResponse { Id = product.Id };
    }
}
