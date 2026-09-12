using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.UpdateWorkRate;

public class UpdateWorkRateCommandHandler : IRequestHandler<UpdateWorkRateCommand, UpdateWorkRateResponse>
{
    private readonly IWorkRateRepository _repository;

    public UpdateWorkRateCommandHandler(IWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateWorkRateResponse> Handle(UpdateWorkRateCommand request, CancellationToken cancellationToken)
    {
        WorkRate? workRate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (workRate == null)
        {
            return new UpdateWorkRateResponse { Found = false };
        }

        workRate.IsActive = request.IsActive;
        workRate.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateWorkRateResponse { Found = true };
    }
}
