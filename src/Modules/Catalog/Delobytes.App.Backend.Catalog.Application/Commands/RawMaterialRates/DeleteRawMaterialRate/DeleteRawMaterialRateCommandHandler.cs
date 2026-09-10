using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.RawMaterialRates.DeleteRawMaterialRate;

public class DeleteRawMaterialRateCommandHandler : IRequestHandler<DeleteRawMaterialRateCommand, DeleteRawMaterialRateResponse>
{
    private readonly IRawMaterialRateRepository _repository;

    public DeleteRawMaterialRateCommandHandler(IRawMaterialRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteRawMaterialRateResponse> Handle(DeleteRawMaterialRateCommand request, CancellationToken cancellationToken)
    {
        RawMaterialRate? rate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (rate == null)
        {
            return new DeleteRawMaterialRateResponse { Found = false };
        }

        // Soft-delete: preserve the record for historical margin calculation snapshots.
        rate.IsActive = false;
        rate.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteRawMaterialRateResponse { Found = true };
    }
}
