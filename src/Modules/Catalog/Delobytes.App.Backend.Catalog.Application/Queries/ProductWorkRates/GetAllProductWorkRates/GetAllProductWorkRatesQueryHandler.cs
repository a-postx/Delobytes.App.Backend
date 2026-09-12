using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductWorkRates.GetAllProductWorkRates;

public class GetAllProductWorkRatesQueryHandler : IRequestHandler<GetAllProductWorkRatesQuery, GetAllProductWorkRatesResponse>
{
    private readonly IProductWorkRateRepository _repository;

    public GetAllProductWorkRatesQueryHandler(IProductWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetAllProductWorkRatesResponse> Handle(GetAllProductWorkRatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductWorkRate> rates = await _repository.GetAllAsync(cancellationToken);

        return new GetAllProductWorkRatesResponse
        {
            Items = rates.Select(r => new ProductWorkRateDto
            {
                Id = r.Id,
                ProductId = r.ProductId,
                AssemblyRatePerDay = r.AssemblyRatePerDay,
                ValidFrom = r.ValidFrom,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
            }).ToList(),
        };
    }
}
