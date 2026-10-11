using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Catalog.Domain.Enums;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.DeleteProductWorkRate;

public class DeleteProductWorkRateCommandHandler : IRequestHandler<DeleteProductWorkRateCommand, DeleteProductWorkRateResponse>
{
    private readonly IProductWorkRateRepository _repository;
    private readonly IProductCostSnapshotService _snapshotService;

    public DeleteProductWorkRateCommandHandler(
        IProductWorkRateRepository repository,
        IProductCostSnapshotService snapshotService)
    {
        _repository = repository;
        _snapshotService = snapshotService;
    }

    public async Task<DeleteProductWorkRateResponse> Handle(DeleteProductWorkRateCommand request, CancellationToken cancellationToken)
    {
        ProductWorkRate? rate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (rate == null)
        {
            return new DeleteProductWorkRateResponse { Found = false };
        }

        // Idempotent: a second submit must not move RemovedFrom forward, which would extend the
        // period during which the version is still charged. A response, not an exception -- the
        // end state the caller asked for is the one already in place.
        if (rate.Status == ProductWorkRateStatus.Removed)
        {
            return new DeleteProductWorkRateResponse { Found = true };
        }

        DateOnly today = DateOnly.FromDateTime(DateTimeOffset.UtcNow.UtcDateTime);

        // A version can only stop being in force on a date on which it had already started: removing
        // a future-dated version on its own start date would claim it was never in force, while
        // setting the date to "today" for such a version would resolve it as effective for a span
        // that ends before it begins.
        DateOnly removedFrom = today > rate.ValidFrom ? today : rate.ValidFrom.AddDays(1);

        // Captured before the mutation, like Create and Update: removal changes the labour component
        // of the cost, and skipping the capture would leave a hole in the snapshot history for
        // exactly the products this operation affects. A different reason is used because "the rate
        // was withdrawn" and "the rate was corrected" are separate events in the history feed.
        await _snapshotService.CaptureBeforeChangeAsync(new[] { rate.ProductId }, "WorkRateRemoved", cancellationToken);

        rate.Status = ProductWorkRateStatus.Removed;
        rate.RemovedFrom = removedFrom;
        rate.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteProductWorkRateResponse { Found = true };
    }
}
