using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates;

/// <summary>
/// Adds a new daily wage version for an existing work rate. The previous active
/// version is deactivated; no existing wage record is ever overwritten.
/// </summary>
public class CreateWorkRateVersionCommand : IRequest<CreateWorkRateVersionResponse>, IRequireRole
{
    public Guid WorkRateId { get; set; }

    public decimal DailyWage { get; set; }

    public DateOnly ValidFrom { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class CreateWorkRateVersionResponse
{
    public Guid Id { get; set; }

    public bool Found { get; set; }
}

public class CreateWorkRateVersionCommandHandler : IRequestHandler<CreateWorkRateVersionCommand, CreateWorkRateVersionResponse>
{
    private readonly IWorkRateRepository _repository;
    private readonly IWorkRateVersionRepository _versionRepository;
    private readonly IProductWorkRateRepository _productWorkRateRepository;
    private readonly IProductCostSnapshotService _snapshotService;

    public CreateWorkRateVersionCommandHandler(
        IWorkRateRepository repository,
        IWorkRateVersionRepository versionRepository,
        IProductWorkRateRepository productWorkRateRepository,
        IProductCostSnapshotService snapshotService)
    {
        _repository = repository;
        _versionRepository = versionRepository;
        _productWorkRateRepository = productWorkRateRepository;
        _snapshotService = snapshotService;
    }

    public async Task<CreateWorkRateVersionResponse> Handle(CreateWorkRateVersionCommand request, CancellationToken cancellationToken)
    {
        WorkRate? workRate = await _repository.GetByIdAsync(request.WorkRateId, cancellationToken);

        if (workRate == null)
        {
            return new CreateWorkRateVersionResponse { Found = false };
        }

        IReadOnlyList<Guid> affectedProductIds = await _productWorkRateRepository.GetProductIdsByWorkRateIdAsync(
            request.WorkRateId, cancellationToken);

        await _snapshotService.CaptureBeforeChangeAsync(affectedProductIds, "WorkRateChanged", cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        WorkRateVersion? activeVersion = await _versionRepository.GetActiveByWorkRateIdAsync(workRate.Id, cancellationToken);
        if (activeVersion != null)
        {
            activeVersion.IsActive = false;
            activeVersion.UpdatedAt = now;
        }

        WorkRateVersion version = new WorkRateVersion
        {
            Id = Guid.NewGuid(),
            WorkRateId = workRate.Id,
            DailyWage = request.DailyWage,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = now,
        };

        _versionRepository.Add(version);

        workRate.UpdatedAt = now;

        await _versionRepository.SaveChangesAsync(cancellationToken);

        return new CreateWorkRateVersionResponse { Id = version.Id, Found = true };
    }
}
