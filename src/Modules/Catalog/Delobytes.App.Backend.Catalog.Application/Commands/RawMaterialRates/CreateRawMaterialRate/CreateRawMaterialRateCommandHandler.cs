using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.RawMaterialRates.CreateRawMaterialRate;

public class CreateRawMaterialRateCommandHandler : IRequestHandler<CreateRawMaterialRateCommand, CreateRawMaterialRateResponse>
{
    private readonly IRawMaterialRateRepository _repository;

    public CreateRawMaterialRateCommandHandler(IRawMaterialRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateRawMaterialRateResponse> Handle(CreateRawMaterialRateCommand request, CancellationToken cancellationToken)
    {
        // Each new rate creates a versioned record; previous records are never modified.
        RawMaterialRate rate = new RawMaterialRate
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            CostPerUnit = request.CostPerUnit,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(rate);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateRawMaterialRateResponse { Id = rate.Id };
    }
}
