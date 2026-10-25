using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates;

public class GetProductWorkRatesQuery : IRequest<GetProductWorkRatesResponse>
{
    public Guid ProductId { get; set; }
}

public class GetProductWorkRatesResponse
{
    public List<ProductWorkRateDto> Items { get; init; } = [];
}

public class GetProductWorkRatesQueryHandler : IRequestHandler<GetProductWorkRatesQuery, GetProductWorkRatesResponse>
{
    private readonly IProductWorkRateRepository _repository;

    public GetProductWorkRatesQueryHandler(IProductWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetProductWorkRatesResponse> Handle(GetProductWorkRatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductWorkRate> rates = await _repository.GetByProductIdAsync(request.ProductId, cancellationToken);

        return new GetProductWorkRatesResponse
        {
            Items = rates.Select(r => new ProductWorkRateDto
            {
                Id = r.Id,
                ProductId = r.ProductId,
                ProductName = r.Product.Name,
                ProductSku = r.Product.Sku,
                WorkRateId = r.WorkRateId,
                AssemblyRatePerDay = r.AssemblyRatePerDay,
                ValidFrom = r.ValidFrom,
                Status = r.Status,
                RemovedFrom = r.RemovedFrom,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            }).ToList(),
        };
    }
}
