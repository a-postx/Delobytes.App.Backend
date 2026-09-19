using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.UpdateProductChannelCost;

public class UpdateProductChannelCostCommandHandler : IRequestHandler<UpdateProductChannelCostCommand, UpdateProductChannelCostResponse>
{
    private readonly IProductChannelCostRepository _repository;

    public UpdateProductChannelCostCommandHandler(IProductChannelCostRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpdateProductChannelCostResponse> Handle(UpdateProductChannelCostCommand request, CancellationToken cancellationToken)
    {
        ProductChannelCost? cost = await _repository.GetByIdAsync(request.Id, cancellationToken);

        if (cost == null)
        {
            return new UpdateProductChannelCostResponse { Found = false };
        }

        cost.Amount = request.Amount;
        cost.UpdatedAt = DateTimeOffset.UtcNow;

        await _repository.SaveChangesAsync(cancellationToken);

        return new UpdateProductChannelCostResponse { Found = true };
    }
}
