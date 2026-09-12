using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductWorkRates.DeleteProductWorkRate;

public class DeleteProductWorkRateCommandHandler : IRequestHandler<DeleteProductWorkRateCommand, DeleteProductWorkRateResponse>
{
    private readonly IProductWorkRateRepository _repository;

    public DeleteProductWorkRateCommandHandler(IProductWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteProductWorkRateResponse> Handle(DeleteProductWorkRateCommand request, CancellationToken cancellationToken)
    {
        ProductWorkRate? rate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (rate == null)
        {
            return new DeleteProductWorkRateResponse { Found = false };
        }

        rate.IsActive = false;
        rate.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteProductWorkRateResponse { Found = true };
    }
}
