using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates.CreateWorkRate;

public class CreateWorkRateCommandHandler : IRequestHandler<CreateWorkRateCommand, CreateWorkRateResponse>
{
    private readonly IWorkRateRepository _repository;
    private readonly IWorkRateVersionRepository _versionRepository;

    public CreateWorkRateCommandHandler(
        IWorkRateRepository repository,
        IWorkRateVersionRepository versionRepository)
    {
        _repository = repository;
        _versionRepository = versionRepository;
    }

    public async Task<CreateWorkRateResponse> Handle(CreateWorkRateCommand request, CancellationToken cancellationToken)
    {
        WorkRate workRate = new WorkRate
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        WorkRateVersion version = new WorkRateVersion
        {
            Id = Guid.NewGuid(),
            WorkRateId = workRate.Id,
            DailyWage = request.DailyWage,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(workRate);
        _versionRepository.Add(version);

        // Both repositories share the scoped CatalogDbContext, so one SaveChanges persists the work rate and its first wage version atomically.
        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateWorkRateResponse { Id = workRate.Id };
    }
}
