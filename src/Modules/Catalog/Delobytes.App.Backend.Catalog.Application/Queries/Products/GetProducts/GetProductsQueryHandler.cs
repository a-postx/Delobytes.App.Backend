using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, GetProductsResponse>
{
    private readonly IProductRepository _repository;
    private readonly IProductPhotoService _photoService;

    public GetProductsQueryHandler(IProductRepository repository, IProductPhotoService photoService)
    {
        _repository = repository;
        _photoService = photoService;
    }

    public async Task<GetProductsResponse> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<Product> products = await _repository.GetAllByStatusAsync(request.Status, cancellationToken);

        return new GetProductsResponse
        {
            Items = products.Select(p => new ProductItem
            {
                Id = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                Description = p.Description,
                Status = p.Status,
                CreatedAt = p.CreatedAt,
                UpdatedAt = p.UpdatedAt,
                ArchivedAt = p.ArchivedAt,
                DeletionRequestedAt = p.DeletionRequestedAt,
                DeletedAt = p.DeletedAt,
                Barcodes = p.Barcodes
                    .Select(b => new ProductBarcodeDto
                    {
                        Id = b.Id,
                        Value = b.Value,
                        Type = b.Type,
                        IsDefault = b.IsDefault,
                    })
                    .ToList(),
                // The list view does not show dimensions; the single-product endpoint does.
                PackingUnit = null,
                // The list view returns only thumbnail photos; the single-product endpoint returns all variants.
                Photos = p.Photos
                    .Where(ph => ph.Status == ProductPhotoStatus.Uploaded && ph.SizeVariant == "thumbnail")
                    .OrderBy(ph => ph.DisplayOrder)
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
            }).ToList(),
        };
    }
}
