using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRate;

public class GetWorkRateQueryHandler : IRequestHandler<GetWorkRateQuery, GetWorkRateResponse?>
{
    private readonly IWorkRateRepository _repository;

    public GetWorkRateQueryHandler(IWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetWorkRateResponse?> Handle(GetWorkRateQuery request, CancellationToken cancellationToken)
    {
        WorkRate? workRate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (workRate == null)
        {
            return null;
        }

        return new GetWorkRateResponse
        {
            Id = workRate.Id,
            Name = workRate.Name,
            DailyWage = workRate.DailyWage,
            ValidFrom = workRate.ValidFrom,
            IsActive = workRate.IsActive,
            CreatedAt = workRate.CreatedAt,
            UpdatedAt = workRate.UpdatedAt,
        };
    }
}
