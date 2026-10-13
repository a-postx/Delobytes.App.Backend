using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Components.GetComponents;

public class GetComponentsQueryHandler : IRequestHandler<GetComponentsQuery, GetComponentsResponse>
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;
    private const int MinPageSize = 1;

    private readonly IComponentRepository _repository;

    public GetComponentsQueryHandler(IComponentRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetComponentsResponse> Handle(GetComponentsQuery request, CancellationToken cancellationToken)
    {
        // No page requested means "give me everything" — the legacy contract that the product
        // BOM editor still relies on. Paging is applied only when a page number arrives.
        int page = Math.Max(request.Page ?? 1, 1);
        int pageSize = Math.Clamp(request.PageSize ?? DefaultPageSize, MinPageSize, MaxPageSize);
        int? skip = request.Page.HasValue ? (page - 1) * pageSize : null;
        int? take = request.Page.HasValue ? pageSize : null;

        bool descending = string.Equals(request.SortDir, "desc", StringComparison.OrdinalIgnoreCase);

        (int totalCount, IReadOnlyList<Component> components) = await _repository.GetPagedAsync(
            skip,
            take,
            request.SortBy,
            descending,
            cancellationToken,
            request.Search);

        return new GetComponentsResponse
        {
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            Items = components.Select(c => new ComponentItem
            {
                Id = c.Id,
                Name = c.Name,
                Description = c.Description,
                Unit = c.Unit,
                Category = c.Category,
                ActivePrice = ComponentPriceMapper.Map(c.Prices.FirstOrDefault(p => p.IsActive)),
                IsActive = c.IsActive,
                CreatedAt = c.CreatedAt,
            }).ToList(),
        };
    }
}
