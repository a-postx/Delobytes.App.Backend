using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRates;

public class GetWorkRatesQueryHandler : IRequestHandler<GetWorkRatesQuery, GetWorkRatesResponse>
{
    private readonly IWorkRateRepository _repository;

    public GetWorkRatesQueryHandler(IWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetWorkRatesResponse> Handle(GetWorkRatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkRate> workRates = await _repository.GetAllAsync(cancellationToken);

        return new GetWorkRatesResponse
        {
            Items = workRates.Select(wr => new WorkRateItem
            {
                Id = wr.Id,
                Name = wr.Name,
                DailyWage = wr.DailyWage,
                AssemblyRatePerDay = wr.AssemblyRatePerDay,
                ValidFrom = wr.ValidFrom,
                IsActive = wr.IsActive,
                CreatedAt = wr.CreatedAt,
            }).ToList(),
        };
    }
}
