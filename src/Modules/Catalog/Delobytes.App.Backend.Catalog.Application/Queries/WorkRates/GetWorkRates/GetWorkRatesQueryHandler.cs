using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Queries.WorkRates;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates.GetWorkRates;

public class GetWorkRatesQueryHandler : IRequestHandler<GetWorkRatesQuery, GetWorkRatesResponse>
{
    private readonly IWorkRateRepository _repository;
    private readonly IWorkRateVersionRepository _versionRepository;

    public GetWorkRatesQueryHandler(IWorkRateRepository repository, IWorkRateVersionRepository versionRepository)
    {
        _repository = repository;
        _versionRepository = versionRepository;
    }

    public async Task<GetWorkRatesResponse> Handle(GetWorkRatesQuery request, CancellationToken cancellationToken)
    {
        IReadOnlyList<WorkRate> workRates = await _repository.GetAllAsync(cancellationToken);

        // One extra round trip for the whole page instead of one per row: active wage versions
        // are resolved for every work rate id at once, mirroring IComponentRepository.GetByIdsAsync.
        IReadOnlyDictionary<Guid, WorkRateVersion> activeVersionsByWorkRateId = await _versionRepository
            .GetActiveByWorkRateIdsAsync(workRates.Select(wr => wr.Id).ToList(), cancellationToken);

        return new GetWorkRatesResponse
        {
            Items = workRates.Select(wr =>
            {
                activeVersionsByWorkRateId.TryGetValue(wr.Id, out WorkRateVersion? activeVersion);

                return new WorkRateItem
                {
                    Id = wr.Id,
                    Name = wr.Name,
                    DailyWage = activeVersion?.DailyWage ?? 0m,
                    ValidFrom = activeVersion?.ValidFrom ?? default,
                    ActiveVersion = WorkRateVersionMapper.Map(activeVersion),
                    IsActive = wr.IsActive,
                    CreatedAt = wr.CreatedAt,
                };
            }).ToList(),
        };
    }
}
