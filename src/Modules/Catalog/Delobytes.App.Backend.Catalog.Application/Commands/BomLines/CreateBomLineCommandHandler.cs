using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Authorization;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;
using Delobytes.App.Backend.Contracts.Identity;

namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines;

public class CreateBomLineCommand : IRequest<CreateBomLineResponse>, IRequireRole
{
    public Guid ProductId { get; set; }
    public Guid ComponentId { get; set; }
    public decimal Quantity { get; set; }
    public DateOnly ValidFrom { get; set; }
    public Role[] AllowedRoles => new[] { Role.Manager, Role.Administrator };
}

public class CreateBomLineResponse
{
    public Guid Id { get; set; }
}

public class CreateBomLineCommandHandler : IRequestHandler<CreateBomLineCommand, CreateBomLineResponse>
{
    private readonly IBomLineRepository _repository;
    private readonly IComponentRepository _componentRepository;
    private readonly IProductCostSnapshotService _snapshotService;

    public CreateBomLineCommandHandler(
        IBomLineRepository repository,
        IComponentRepository componentRepository,
        IProductCostSnapshotService snapshotService)
    {
        _repository = repository;
        _componentRepository = componentRepository;
        _snapshotService = snapshotService;
    }

    public async Task<CreateBomLineResponse> Handle(CreateBomLineCommand request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0)
        {
            throw new AppException(CatalogErrorCodes.BomLineInvalidQuantity);
        }

        Component? component = await _componentRepository.GetByIdAsync(request.ComponentId, cancellationToken) ?? throw new AppException(CatalogErrorCodes.BomComponentNotFound);

        await _snapshotService.CaptureBeforeChangeAsync(new[] { request.ProductId }, "BomChanged", cancellationToken);

        IReadOnlyList<BomLine> open = await _repository.GetOpenByProductIdAsync(request.ProductId, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BomLine? previous = open.FirstOrDefault(b => b.ComponentId == request.ComponentId);

        if (previous != null)
        {
            // The caller may post a future-dated version, and the current one has to keep applying
            // until that date. Closing it at "today" — which is what a flag-based update did — would
            // create a gap in which the component is silently absent, and closing it at the new
            // ValidFrom only makes sense if the new version starts later.
            if (request.ValidFrom <= previous.ValidFrom)
            {
                throw new AppException(CatalogErrorCodes.BomLineValidFromConflict);
            }

            previous.CloseAt(request.ValidFrom);
            previous.UpdatedAt = now;
        }

        BomLine line = new BomLine {
            Id = EntityId.New(),
            ProductId = request.ProductId,
            ComponentId = request.ComponentId,
            Quantity = request.Quantity,
            ValidFrom = request.ValidFrom,
            CreatedAt = now
        };

        _repository.Add(line);

        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateBomLineResponse { Id = line.Id };
    }
}
