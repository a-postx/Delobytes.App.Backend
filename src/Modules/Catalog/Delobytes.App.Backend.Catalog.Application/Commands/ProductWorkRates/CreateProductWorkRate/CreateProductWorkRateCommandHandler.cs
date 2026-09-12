using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.CreateProductWorkRate;

public class CreateProductWorkRateCommandHandler : IRequestHandler<CreateProductWorkRateCommand, CreateProductWorkRateResponse>
{
    private readonly IProductWorkRateRepository _repository;

    public CreateProductWorkRateCommandHandler(IProductWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateProductWorkRateResponse> Handle(CreateProductWorkRateCommand request, CancellationToken cancellationToken)
    {
        // Each new rate creates a versioned record; previous records are never modified.
        ProductWorkRate rate = new ProductWorkRate
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
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
