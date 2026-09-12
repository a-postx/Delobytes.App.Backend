using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRate;

public class CreateWorkRateCommandHandler : IRequestHandler<CreateWorkRateCommand, CreateWorkRateResponse>
{
    private readonly IWorkRateRepository _repository;

    public CreateWorkRateCommandHandler(IWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateWorkRateResponse> Handle(CreateWorkRateCommand request, CancellationToken cancellationToken)
    {
        WorkRate workRate = new WorkRate
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            DailyWage = request.DailyWage,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(workRate);
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateWorkRateResponse { Id = workRate.Id };
    }
}
