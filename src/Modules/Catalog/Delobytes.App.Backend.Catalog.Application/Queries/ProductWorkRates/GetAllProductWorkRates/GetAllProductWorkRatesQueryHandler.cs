using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetAllProductWorkRates;

public class GetAllProductWorkRatesQueryHandler : IRequestHandler<GetAllProductWorkRatesQuery, GetAllProductWorkRatesResponse>
{
    private const int DefaultPageSize = 25;
    private const int MinPageSize = 1;
    private const int MaxPageSize = 200;

    private readonly IProductWorkRateRepository _repository;

    public GetAllProductWorkRatesQueryHandler(IProductWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetAllProductWorkRatesResponse> Handle(
        GetAllProductWorkRatesQuery request,
        CancellationToken cancellationToken)
    {
        // No page requested means "give me everything": the legacy contract, kept for clients that
        // still expect the full list.
        int page = Math.Max(request.Page ?? 1, 1);
        int pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        int? skipProducts = request.Page.HasValue ? (page - 1) * pageSize : null;
        int? takeProducts = request.Page.HasValue ? pageSize : null;

        bool descending = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        (int totalCount, IReadOnlyList<ProductWorkRate> rates) = await _repository.GetPagedByProductAsync(
            request.Status,
            request.Search,
            skipProducts,
            takeProducts,
            request.SortBy,
            descending,
            cancellationToken);

        GetAllProductWorkRatesResponse response = new()
        {
            Items = rates.Select(Map).ToList(),
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
        };

        if (request.IncludeCounts)
        {
            // Counted without the status filter: these are the numbers the filter itself offers.
            (int active, int inactive, int all) = await _repository.GetGroupCountsAsync(
                request.Search,
                cancellationToken);

            response = new GetAllProductWorkRatesResponse
            {
                Items = response.Items,
                TotalCount = response.TotalCount,
                Page = response.Page,
                PageSize = response.PageSize,
                StatusCounts = new ProductWorkRateGroupCounts
                {
                    Active = active,
                    Inactive = inactive,
                    All = all,
                },
            };
        }

        return response;
    }

    private static ProductWorkRateDto Map(ProductWorkRate rate)
    {
        return new ProductWorkRateDto
        {
            Id = rate.Id,
            ProductId = rate.ProductId,
            ProductName = rate.Product.Name,
            ProductSku = rate.Product.Sku,
            WorkRateId = rate.WorkRateId,
            AssemblyRatePerDay = rate.AssemblyRatePerDay,
            ValidFrom = rate.ValidFrom,
            IsActive = rate.IsActive,
            CreatedAt = rate.CreatedAt,
            UpdatedAt = rate.UpdatedAt,
        };
    }
}
