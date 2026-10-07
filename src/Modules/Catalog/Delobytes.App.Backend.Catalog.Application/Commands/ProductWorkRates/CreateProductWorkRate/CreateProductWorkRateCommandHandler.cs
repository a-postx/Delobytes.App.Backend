using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;

public class CreateProductWorkRateCommandHandler : IRequestHandler<CreateProductWorkRateCommand, CreateProductWorkRateResponse>
{
    private readonly IProductWorkRateRepository _repository;
    private readonly IWorkRateRepository _workRateRepository;
    private readonly IProductCostSnapshotService _snapshotService;

    public CreateProductWorkRateCommandHandler(
        IProductWorkRateRepository repository,
        IWorkRateRepository workRateRepository,
        IProductCostSnapshotService snapshotService)
    {
        _repository = repository;
        _workRateRepository = workRateRepository;
        _snapshotService = snapshotService;
    }

    public async Task<CreateProductWorkRateResponse> Handle(CreateProductWorkRateCommand request, CancellationToken cancellationToken)
    {
        WorkRate? workRate = await _workRateRepository.GetByIdAsync(request.WorkRateId, cancellationToken);

        if (workRate == null)
        {
            throw new AppException(ErrorCodes.Catalog.WorkRateNotFound);
        }

        // Checked before any snapshot work: a duplicate ValidFrom would resolve unpredictably via
        // the ThenByDescending(CreatedAt) tie-break in GetEffectiveAtAsync, so the request is
        // rejected outright rather than silently accepted.
        IReadOnlyList<ProductWorkRate> existingVersions =
            await _repository.GetByProductIdAsync(request.ProductId, cancellationToken);

        if (existingVersions.Any(v => v.ValidFrom == request.ValidFrom))
        {
            throw new AppException(ErrorCodes.Catalog.ProductWorkRateValidFromConflict);
        }

        await _snapshotService.CaptureBeforeChangeAsync(new[] { request.ProductId }, "WorkRateChanged", cancellationToken);

        // Supersede the version currently in force, mirroring CreateComponentPriceCommandHandler:
        // without this, several rows per product could stay IsActive = true simultaneously.
        ProductWorkRate? activeRate = await _repository.GetActiveByProductIdAsync(request.ProductId, cancellationToken);

        DateTimeOffset now = DateTimeOffset.UtcNow;

        if (activeRate != null)
        {
            activeRate.IsActive = false;
            activeRate.UpdatedAt = now;
        }

        ProductWorkRate rate = new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            WorkRateId = request.WorkRateId,
            AssemblyRatePerDay = request.AssemblyRatePerDay,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = now,
        };

        // The deactivation and the insert commit in the same SaveChangesAsync call: with RowVersion
        // now in place, a failed concurrency check on the deactivated row must not leave the new
        // row persisted without it, which would otherwise produce two active versions.
        _repository.Add(rate);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateProductWorkRateResponse { Id = rate.Id };
    }
}
