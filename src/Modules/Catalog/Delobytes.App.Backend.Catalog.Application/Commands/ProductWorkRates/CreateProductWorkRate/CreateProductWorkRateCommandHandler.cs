using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;

public class CreateProductWorkRateCommandHandler : IRequestHandler<CreateProductWorkRateCommand, CreateProductWorkRateResponse>
{
    private readonly IProductWorkRateRepository _repository;
    private readonly IWorkRateRepository _workRateRepository;

    public CreateProductWorkRateCommandHandler(
        IProductWorkRateRepository repository,
        IWorkRateRepository workRateRepository)
    {
        _repository = repository;
        _workRateRepository = workRateRepository;
    }

    public async Task<CreateProductWorkRateResponse> Handle(CreateProductWorkRateCommand request, CancellationToken cancellationToken)
    {
        // The reference must exist, but a deactivated rate is accepted: rates are versioned and a
        // product rate may legitimately be backdated to a period when that version was the effective one.
        WorkRate? workRate = await _workRateRepository.GetByIdAsync(request.WorkRateId, cancellationToken);

        if (workRate == null)
        {
            throw new AppException(ErrorCodes.Catalog.WorkRateNotFound);
        }

        // Each new rate creates a versioned record; previous records are never modified.
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
