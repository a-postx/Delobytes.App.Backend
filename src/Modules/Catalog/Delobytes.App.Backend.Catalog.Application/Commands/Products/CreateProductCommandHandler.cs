using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Numbering;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.Products;

public class CreateProductCommand : IRequest<CreateProductResponse>
{
    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public List<ProductBarcodeDto>? Barcodes { get; set; }

    public PackingUnitDto? PackingUnit { get; set; }
}

public class CreateProductResponse
{
    public Guid Id { get; set; }

    public string Code { get; set; } = default!;
}

public class CreateProductCommandHandler : IRequestHandler<CreateProductCommand, CreateProductResponse>
{
    private readonly IProductRepository _repository;
    private readonly INumberingSchemeProvider _numberingSchemes;
    private readonly INumberAllocator _numberAllocator;
    private readonly ICatalogTransactionFactory _transactionFactory;

    public CreateProductCommandHandler(
        IProductRepository repository,
        INumberingSchemeProvider numberingSchemes,
        INumberAllocator numberAllocator,
        ICatalogTransactionFactory transactionFactory)
    {
        _repository = repository;
        _numberingSchemes = numberingSchemes;
        _numberAllocator = numberAllocator;
        _transactionFactory = transactionFactory;
    }

    public async Task<CreateProductResponse> Handle(CreateProductCommand request, CancellationToken cancellationToken)
    {
        await using ICatalogTransaction transaction = await _transactionFactory.BeginAsync(cancellationToken);

        try
        {
            // Allocated inside the transaction, so the number becomes durable only together with
            // the product; a create rejected later in this method rolls the counter back with it.
            NumberingScheme scheme = await _numberingSchemes.GetDefaultProductSchemeAsync(cancellationToken);
            string code = await _numberAllocator.AllocateAsync(scheme.Id, cancellationToken);

            if (await _repository.SkuExistsAsync(request.Sku, cancellationToken))
            {
                throw new AppException(ErrorCodes.Catalog.ProductSkuConflict);
            }

            Product product = new Product
            {
                Id = Guid.NewGuid(),
                Code = code,
                Sku = request.Sku,
                Name = request.Name,
                Description = request.Description,
                Status = ProductStatus.Active
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
                        IsDefault = dto.IsDefault
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
            await transaction.CommitAsync(cancellationToken);

            return new CreateProductResponse { Id = product.Id, Code = product.Code };
        }
        catch
        {
            // Cancellation must not prevent the rollback: a cancelled token here would leave the
            // allocation and the transaction open until the scope is disposed.
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
