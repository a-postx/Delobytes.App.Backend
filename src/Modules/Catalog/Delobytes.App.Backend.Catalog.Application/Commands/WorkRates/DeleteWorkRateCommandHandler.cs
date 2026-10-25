using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Authorization;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.WorkRates;

public class DeleteWorkRateCommand : IRequest<DeleteWorkRateResponse>, IRequireRole
{
    public Guid Id { get; set; }

    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class DeleteWorkRateResponse
{
    public bool Found { get; set; }
}

public class DeleteWorkRateCommandHandler : IRequestHandler<DeleteWorkRateCommand, DeleteWorkRateResponse>
{
    private readonly IWorkRateRepository _repository;

    public DeleteWorkRateCommandHandler(IWorkRateRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteWorkRateResponse> Handle(DeleteWorkRateCommand request, CancellationToken cancellationToken)
    {
        WorkRate? workRate = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (workRate == null)
        {
            return new DeleteWorkRateResponse { Found = false };
        }

        workRate.IsActive = false;
        workRate.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteWorkRateResponse { Found = true };
    }
}
