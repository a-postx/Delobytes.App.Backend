using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines.CreateBomLine;

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
            throw new AppException(ErrorCodes.Catalog.BomLineInvalidQuantity);
        }

        Component? component = await _componentRepository.GetByIdAsync(request.ComponentId, cancellationToken);

        if (component == null) {
            throw new AppException(ErrorCodes.Catalog.BomComponentNotFound);
        }

        await _snapshotService.CaptureBeforeChangeAsync(new[] { request.ProductId }, "BomChanged", cancellationToken);

        IReadOnlyList<BomLine> active = await _repository.GetActiveByProductIdAsync(request.ProductId, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BomLine? previous = active.FirstOrDefault(b => b.ComponentId == request.ComponentId);

        if (previous != null)
        {
            // The replacement starts on the date the caller asked for, so the version it supersedes
            // has to end there too; any other boundary would leave the two overlapping.
            previous.CloseAt(request.ValidFrom);
            previous.UpdatedAt = now;
        }

        BomLine line = new BomLine {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            ComponentId = request.ComponentId,
            Quantity = request.Quantity,
            ValidFrom = request.ValidFrom,
            IsActive = true,
            CreatedAt = now
        };

        _repository.Add(line);

        await _repository.SaveChangesAsync(cancellationToken);

        return new CreateBomLineResponse { Id = line.Id };
    }
}
