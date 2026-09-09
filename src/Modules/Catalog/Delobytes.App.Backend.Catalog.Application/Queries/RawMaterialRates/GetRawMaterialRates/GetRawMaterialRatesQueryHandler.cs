using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;

public class GetRawMaterialRatesQueryHandler : IRequestHandler<GetRawMaterialRatesQuery, GetRawMaterialRatesResponse>
{
    private readonly IRawMaterialRateRepository _repository;

    public GetRawMaterialRatesQueryHandler(IRawMaterialRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetRawMaterialRatesResponse> Handle(GetRawMaterialRatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<RawMaterialRate> rates = await _repository.GetByProductIdAsync(request.ProductId, cancellationToken);

        return new GetRawMaterialRatesResponse
        {
            Items = rates.Select(r => new RawMaterialRateItem
            {
                Id = r.Id,
                ProductId = r.ProductId,
                CostPerUnit = r.CostPerUnit,
                ValidFrom = r.ValidFrom,
                CreatedAt = r.CreatedAt,
            }).ToList(),
        };
    }
}
