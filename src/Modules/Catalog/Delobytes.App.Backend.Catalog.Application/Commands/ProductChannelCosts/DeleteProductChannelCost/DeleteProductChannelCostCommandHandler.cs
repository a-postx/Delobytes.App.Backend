using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.DeleteProductChannelCost;

public class DeleteProductChannelCostCommandHandler
    : IRequestHandler<DeleteProductChannelCostCommand, DeleteProductChannelCostResponse>
{
    private readonly IProductChannelCostRepository _repository;

    public DeleteProductChannelCostCommandHandler(IProductChannelCostRepository repository)
    {
        _repository = repository;
    }

    public async Task<DeleteProductChannelCostResponse> Handle(
        DeleteProductChannelCostCommand request,
        CancellationToken cancellationToken)
    {
        ProductChannelCost? cost = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (cost == null)
        {
            return new DeleteProductChannelCostResponse { Found = false };
        }

        _repository.Remove(cost);
        await _repository.SaveChangesAsync(cancellationToken);

        return new DeleteProductChannelCostResponse { Found = true };
    }
}
