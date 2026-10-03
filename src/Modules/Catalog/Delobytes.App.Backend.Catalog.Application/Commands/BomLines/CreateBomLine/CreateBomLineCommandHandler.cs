using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;
namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines.CreateBomLine;
public class CreateBomLineCommandHandler : IRequestHandler<CreateBomLineCommand, CreateBomLineResponse>
{
    private readonly IBomLineRepository _repository;
    private readonly IComponentRepository _componentRepository;
    public CreateBomLineCommandHandler(IBomLineRepository repository, IComponentRepository componentRepository) { _repository = repository; _componentRepository = componentRepository; }
    public async Task<CreateBomLineResponse> Handle(CreateBomLineCommand request, CancellationToken cancellationToken)
    {
        if (request.Quantity <= 0) { throw new AppException(ErrorCodes.Catalog.BomLineInvalidQuantity); }
        Component? component = await _componentRepository.GetByIdAsync(request.ComponentId, cancellationToken);
        if (component == null) { throw new AppException(ErrorCodes.Catalog.BomComponentNotFound); }
        IReadOnlyList<BomLine> active = await _repository.GetActiveByProductIdAsync(request.ProductId, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        BomLine? previous = active.FirstOrDefault(b => b.ComponentId == request.ComponentId);
        if (previous != null) { previous.IsActive = false; previous.UpdatedAt = now; }
        BomLine line = new BomLine { Id = Guid.NewGuid(), ProductId = request.ProductId, ComponentId = request.ComponentId, Quantity = request.Quantity, ValidFrom = request.ValidFrom, IsActive = true, CreatedAt = now };
        _repository.Add(line);
        await _repository.SaveChangesAsync(cancellationToken);
        return new CreateBomLineResponse { Id = line.Id };
    }
}