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

        await _snapshotService.CaptureBeforeChangeAsync(new[] { request.ProductId }, "WorkRateChanged", cancellationToken);

        ProductWorkRate rate = new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            WorkRateId = request.WorkRateId,
            AssemblyRatePerDay = request.AssemblyRatePerDay,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(rate);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateProductWorkRateResponse { Id = rate.Id };
    }
}
