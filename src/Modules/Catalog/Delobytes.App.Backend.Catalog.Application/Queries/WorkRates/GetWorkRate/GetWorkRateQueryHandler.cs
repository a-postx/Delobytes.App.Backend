using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.WorkRates;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRate;

public class GetWorkRateQueryHandler : IRequestHandler<GetWorkRateQuery, GetWorkRateResponse?>
{
    private readonly IWorkRateRepository _repository;
    private readonly IWorkRateVersionRepository _versionRepository;

    public GetWorkRateQueryHandler(IWorkRateRepository repository, IWorkRateVersionRepository versionRepository)
    {
        _repository = repository;
        _versionRepository = versionRepository;
    }

    public async Task<GetWorkRateResponse?> Handle(GetWorkRateQuery request, CancellationToken cancellationToken)
    {
        WorkRate? workRate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (workRate == null)
        {
            return null;
        }

        WorkRateVersion? activeVersion = await _versionRepository.GetActiveByWorkRateIdAsync(workRate.Id, cancellationToken);

        return new GetWorkRateResponse
        {
            Id = workRate.Id,
            Name = workRate.Name,
            DailyWage = activeVersion?.DailyWage ?? 0m,
            ValidFrom = activeVersion?.ValidFrom ?? default,
            ActiveVersion = WorkRateVersionMapper.Map(activeVersion),
            IsActive = workRate.IsActive,
            CreatedAt = workRate.CreatedAt,
            UpdatedAt = workRate.UpdatedAt,
        };
    }
}
