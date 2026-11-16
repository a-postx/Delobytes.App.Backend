using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using Delobytes.App.Backend.Contracts.Interfaces;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines;

public class DeleteBomLineCommand : IRequest<DeleteBomLineResponse>, IRequireRole
{
    public Guid Id { get; set; }
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class DeleteBomLineResponse { public bool Found { get; set; } }

public class DeleteBomLineCommandHandler : IRequestHandler<DeleteBomLineCommand, DeleteBomLineResponse>
{
    private readonly IBomLineRepository _repository;
    private readonly IProductCostSnapshotService _snapshotService;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantTimeService _tenantTimeService;

    public DeleteBomLineCommandHandler(
        IBomLineRepository repository,
        IProductCostSnapshotService snapshotService,
        ITenantContext tenantContext,
        ITenantTimeService tenantTimeService)
    {
        _repository = repository;
        _snapshotService = snapshotService;
        _tenantContext = tenantContext;
        _tenantTimeService = tenantTimeService;
    }

    public async Task<DeleteBomLineResponse> Handle(DeleteBomLineCommand request, CancellationToken cancellationToken)
    {
        BomLine? line = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (line == null)
        {
            throw new AppException(ErrorCodes.Catalog.BomLineNotFound);
        }

        DateOnly today = await _tenantTimeService.GetTodayAsync(_tenantContext, cancellationToken);

        // A version already closed on or before today cannot be withdrawn again: it is not in force
        // now, and moving its ValidTo would rewrite a period that has already been reported on.
        if (line.ValidTo != null && line.ValidTo <= today)
        {
            throw new AppException(ErrorCodes.Catalog.BomLineNotFound);
        }

        // Captured before the mutation: removal changes the material, logistics and packaging
        // components of the cost at once, and skipping the capture would leave a hole in the snapshot
        // history for exactly the products this operation affects. The reason is distinct because
        // "the component was removed from the composition" and "the composition was replaced" are
        // separate events in the history feed.
        await _snapshotService.CaptureBeforeChangeAsync(new[] { line.ProductId }, "BomLineRemoved", cancellationToken);

        // Dated, not flagged: the component stops counting from this date onwards and keeps counting
        // for every earlier date. A version withdrawn on the day it was meant to start gets an empty
        // interval instead of an inverted one.
        line.CloseAt(today);
        line.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteBomLineResponse { Found = true };
    }
}
