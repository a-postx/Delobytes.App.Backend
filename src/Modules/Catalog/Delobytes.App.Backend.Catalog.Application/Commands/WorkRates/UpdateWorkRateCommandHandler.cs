using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates;

/// <summary>
/// Updates descriptive fields only: name and activity status. The daily wage is versioned through
/// <see cref="CreateWorkRateVersionCommand"/>.
/// </summary>
public class UpdateWorkRateCommand : IRequest<UpdateWorkRateResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public string Name { get; set; } = default!;

    public bool IsActive { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class UpdateWorkRateResponse
{
    public bool Found { get; set; }
}

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

        workRate.Name = request.Name;
        workRate.IsActive = request.IsActive;
        workRate.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateWorkRateResponse { Found = true };
    }
}
