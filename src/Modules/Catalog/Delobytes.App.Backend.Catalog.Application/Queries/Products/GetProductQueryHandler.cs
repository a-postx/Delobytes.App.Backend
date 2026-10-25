using MediatR;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

public class GetProductQuery : IRequest<GetProductResponse>
{
    public Guid Id { get; set; }
}

public class GetProductResponse
{
    public bool Found { get; set; }

    public Guid Id { get; set; }

    public string Sku { get; set; } = default!;

    public string Name { get; set; } = default!;

    public string? Description { get; set; }

    public ProductStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public DateTimeOffset? ArchivedAt { get; set; }

    public DateTimeOffset? DeletionRequestedAt { get; set; }

    public DateTimeOffset? DeletedAt { get; set; }

    // Placeholder for future marketplace import source — manual entry for now
    public string CreationSource { get; set; } = "Manual";

    public List<ProductBarcodeDto>? Barcodes { get; set; }

    public PackingUnitDto? PackingUnit { get; set; }

    public List<ProductPhotoDto>? Photos { get; set; }

    /// <summary>
    /// Links of this product to its external cards on sales channels (ChannelProduct).
    /// Empty/null means the product has no marketplace connections and is fully editable.
    /// </summary>
    public List<ProductChannelLinkDto>? ChannelLinks { get; set; }
}

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
