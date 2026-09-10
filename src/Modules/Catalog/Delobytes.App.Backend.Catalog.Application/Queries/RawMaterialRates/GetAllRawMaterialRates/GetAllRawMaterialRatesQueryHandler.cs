using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetRawMaterialRates;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.RawMaterialRates.GetAllRawMaterialRates;

public class GetAllRawMaterialRatesQueryHandler : IRequestHandler<GetAllRawMaterialRatesQuery, GetAllRawMaterialRatesResponse>
{
    private readonly IRawMaterialRateRepository _repository;

    public GetAllRawMaterialRatesQueryHandler(IRawMaterialRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetAllRawMaterialRatesResponse> Handle(GetAllRawMaterialRatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<RawMaterialRate> rates = await _repository.GetAllAsync(cancellationToken);

        return new GetAllRawMaterialRatesResponse
        {
            Items = rates.Select(r => new RawMaterialRateItem
            {
                Id = r.Id,
                ProductId = r.ProductId,
                CostPerUnit = r.CostPerUnit,
                ValidFrom = r.ValidFrom,
                IsActive = r.IsActive,
                CreatedAt = r.CreatedAt,
            }).ToList(),
        };
    }
}
