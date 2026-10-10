using Delobytes.App.Backend.Catalog.Application.Interfaces;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.Products;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProducts;

public class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, GetProductsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MinPageSize = 1;

    private readonly IProductRepository _repository;
    private readonly IProductPhotoService _photoService;

    public GetProductsQueryHandler(IProductRepository repository, IProductPhotoService photoService)
    {
        _repository = repository;
        _photoService = photoService;
    }

    public async Task<GetProductsResponse> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        // No page requested means "give me everything" — the legacy contract that the work-rate
        // and channel-cost views still rely on. Paging is applied only when a page number arrives.
        int page = Math.Max(request.Page ?? 1, 1);
        int pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        int? skip = request.Page.HasValue ? (page - 1) * pageSize : null;
        int? take = request.Page.HasValue ? pageSize : null;

        bool descending = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        (int totalCount, IReadOnlyList<Product> products) = await _repository.GetPagedAsync(
            request.Status,
            skip,
            take,
            request.SortBy,
            descending,
            cancellationToken,
            request.Search,
            request.IncludeWorkRateCoverage);

        GetProductsResponse response = new()
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = products.Select(p => new ProductItem
            {
                Id = p.Id,
                Sku = p.Sku,
                Name = p.Name,
                Description = p.Description,
                Status = p.Status,
                HasActiveWorkRate = request.IncludeWorkRateCoverage && p.ProductWorkRates.Any(r => r.IsActive),
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
                ChannelLinks = p.ChannelProducts
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
            }).ToList(),
        };

        if (request.IncludeCounts)
        {
            (int active, int archived, int all) = await _repository.GetStatusCountsAsync(cancellationToken, request.Search);
            response.StatusCounts = new ProductStatusCounts
            {
                Active = active,
                Archived = archived,
                All = all,
            };
        }

        return response;
    }
}
