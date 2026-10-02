using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProduct;

public class GetProductQueryHandler : IRequestHandler<GetProductQuery, GetProductResponse>
{
    private readonly IProductRepository _repository;
    private readonly IProductPhotoService _photoService;

    public GetProductQueryHandler(IProductRepository repository, IProductPhotoService photoService)
    {
        _repository = repository;
        _photoService = photoService;
    }

    public async Task<GetProductResponse> Handle(GetProductQuery request, CancellationToken cancellationToken)
    {
        Product? product = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (product == null)
        {
            return new GetProductResponse { Found = false };
        }

        return new GetProductResponse
        {
            Found = true,
            Id = product.Id,
            Sku = product.Sku,
            Name = product.Name,
            Description = product.Description,
            Status = product.Status,
            CreatedAt = product.CreatedAt,
            UpdatedAt = product.UpdatedAt,
            ArchivedAt = product.ArchivedAt,
            DeletionRequestedAt = product.DeletionRequestedAt,
            DeletedAt = product.DeletedAt,
            Barcodes = product.Barcodes
                .Select(b => new ProductBarcodeDto
                {
                    Id = b.Id,
                    Value = b.Value,
                    Type = b.Type,
                    IsDefault = b.IsDefault,
                })
                .ToList(),
            PackingUnit = ToPackingUnitDto(product.PackingUnits.FirstOrDefault(pu => pu.IsActive)),
            Photos = product.Photos
                .Where(ph => ph.Status == ProductPhotoStatus.Uploaded)
                .OrderBy(ph => ph.DisplayOrder)
                .ThenBy(ph => ph.SizeVariant)
                .Select(ph => new ProductPhotoDto
                {
                    Id = ph.Id,
                    DisplayOrder = ph.DisplayOrder,
                    SizeVariant = ph.SizeVariant,
                    Url = _photoService.GetPublicUrl(ph),
                    Width = ph.Width,
                    Height = ph.Height
                })
                .ToList(),
            ChannelLinks = product.ChannelProducts
                .Select(cp => new ProductChannelLinkDto
                {
                    ChannelId = cp.ChannelId,
                    ChannelName = cp.Channel.Name,
                    ChannelCode = cp.Channel.Code,
                    ExternalProductId = cp.ExternalProductId,
                    ExternalSku = cp.ExternalSku,
                    IsActive = cp.IsActive,
                })
                .ToList(),
        };
    }

    private static PackingUnitDto? ToPackingUnitDto(PackingUnit? unit)
    {
        if (unit == null)
        {
            return null;
        }

        return new PackingUnitDto
        {
            LengthCm = unit.LengthCm,
            WidthCm = unit.WidthCm,
            HeightCm = unit.HeightCm,
            WeightKg = unit.WeightKg,
        };
    }
}
