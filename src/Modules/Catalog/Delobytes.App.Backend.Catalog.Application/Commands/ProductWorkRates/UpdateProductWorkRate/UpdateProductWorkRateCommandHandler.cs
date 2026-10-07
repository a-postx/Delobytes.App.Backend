using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.UpdateProductWorkRate;

public class UpdateProductWorkRateCommandHandler : IRequestHandler<UpdateProductWorkRateCommand, UpdateProductWorkRateResponse>
{
    private readonly IProductWorkRateRepository _repository;
    private readonly IWorkRateRepository _workRateRepository;
    private readonly IProductCostSnapshotService _snapshotService;

    public UpdateProductWorkRateCommandHandler(
        IProductWorkRateRepository repository,
        IWorkRateRepository workRateRepository,
        IProductCostSnapshotService snapshotService)
    {
        _repository = repository;
        _workRateRepository = workRateRepository;
        _snapshotService = snapshotService;
    }

    public async Task<UpdateProductWorkRateResponse> Handle(UpdateProductWorkRateCommand request, CancellationToken cancellationToken)
    {
        ProductWorkRate? rate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (rate == null)
        {
            return new UpdateProductWorkRateResponse { Found = false };
        }

        // Editing a superseded or deleted version would never change a calculation: it is not the
        // effective one for any date going forward. Allowing the edit would reintroduce the
        // "silently does nothing" behaviour this task removes.
        if (!rate.IsActive)
        {
            throw new AppException(ErrorCodes.Catalog.ProductWorkRateNotFound);
        }

        if (request.WorkRateId != rate.WorkRateId)
        {
            WorkRate? workRate = await _workRateRepository.GetByIdAsync(request.WorkRateId, cancellationToken);

            if (workRate == null)
            {
                throw new AppException(ErrorCodes.Catalog.WorkRateNotFound);
            }
        }

        List<ProductWorkRate> otherVersions = (await _repository.GetByProductIdAsync(rate.ProductId, cancellationToken))
            .Where(v => v.Id != rate.Id)
            .ToList();

        // No two versions of the same product may share a ValidFrom: the tie-break in
        // GetEffectiveAtAsync (ThenByDescending(CreatedAt)) would otherwise resolve a duplicate
        // unpredictably.
        if (otherVersions.Any(v => v.ValidFrom == request.ValidFrom))
        {
            throw new AppException(ErrorCodes.Catalog.ProductWorkRateValidFromConflict);
        }

        // The edited version must remain the latest one for the product; otherwise an older
        // version would win the effective-date resolution and the correction the user just made
        // would not take effect.
        if (otherVersions.Any(v => v.ValidFrom > request.ValidFrom))
        {
            throw new AppException(ErrorCodes.Catalog.ProductWorkRateValidFromNotLatest);
        }

        await _snapshotService.CaptureBeforeChangeAsync(new[] { rate.ProductId }, "WorkRateChanged", cancellationToken);

        rate.WorkRateId = request.WorkRateId;
        rate.AssemblyRatePerDay = request.AssemblyRatePerDay;
        rate.ValidFrom = request.ValidFrom;
        rate.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateProductWorkRateResponse { Found = true };
    }
}
