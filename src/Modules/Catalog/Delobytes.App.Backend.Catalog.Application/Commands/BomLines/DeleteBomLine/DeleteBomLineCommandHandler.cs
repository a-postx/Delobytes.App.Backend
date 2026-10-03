using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using Delobytes.App.Backend.Contracts.Errors;
using MediatR;
namespace Delobytes.App.Backend.Catalog.Application.Commands.BomLines.DeleteBomLine;
public class DeleteBomLineCommandHandler : IRequestHandler<DeleteBomLineCommand, DeleteBomLineResponse>
{
    private readonly IBomLineRepository _repository;
    public DeleteBomLineCommandHandler(IBomLineRepository repository) { _repository = repository; }
    public async Task<DeleteBomLineResponse> Handle(DeleteBomLineCommand request, CancellationToken cancellationToken)
    {
        BomLine? line = await _repository.GetByIdAsync(request.Id, cancellationToken);
        if (line == null || !line.IsActive) { throw new AppException(ErrorCodes.Catalog.BomLineNotFound); }
        line.IsActive = false; line.UpdatedAt = DateTimeOffset.UtcNow;
        await _repository.SaveChangesAsync(cancellationToken);
        return new DeleteBomLineResponse { Found = true };
    }
}