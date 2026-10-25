using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.WorkRates;

public class GetWorkRateQuery : IRequest<GetWorkRateResponse?>
{
    public Guid Id { get; set; }
}

public class GetWorkRateResponse
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    /// <summary>
    /// Kept for backward compatibility: sourced from <see cref="ActiveVersion"/>, zero when there is none.
    /// </summary>
    public decimal DailyWage { get; set; }

    /// <summary>
    /// Kept for backward compatibility: sourced from <see cref="ActiveVersion"/>, default when there is none.
    /// </summary>
    public DateOnly ValidFrom { get; set; }

    /// <summary>Currently active wage version, or null when the work rate has no active wage.</summary>
    public WorkRateVersionDto? ActiveVersion { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

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
