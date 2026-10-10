using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetProductWorkRates;

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
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt,
            }).ToList(),
        };
    }
}
