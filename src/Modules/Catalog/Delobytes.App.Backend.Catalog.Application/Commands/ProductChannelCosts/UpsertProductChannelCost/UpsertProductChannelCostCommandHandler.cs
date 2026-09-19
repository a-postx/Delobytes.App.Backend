using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.UpsertProductChannelCost;

public class UpsertProductChannelCostCommandHandler
    : IRequestHandler<UpsertProductChannelCostCommand, UpsertProductChannelCostResponse>
{
    private readonly IProductChannelCostRepository _repository;

    public UpsertProductChannelCostCommandHandler(IProductChannelCostRepository repository)
    {
        _repository = repository;
    }

    public async Task<UpsertProductChannelCostResponse> Handle(
        UpsertProductChannelCostCommand request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductChannelCost> existing =
            await _repository.GetByProductAndChannelAsync(request.ProductId, request.ChannelId, cancellationToken);

        ProductChannelCost? match = existing.FirstOrDefault(c => c.CostTypeId == request.CostTypeId);

        if (match != null)
        {
            match.Amount = request.Amount;
            match.UpdatedAt = DateTimeOffset.UtcNow;
            await _repository.SaveChangesAsync(cancellationToken);

            return new UpsertProductChannelCostResponse { Id = match.Id, Created = false };
        }

        ProductChannelCost cost = new ProductChannelCost
        {
            Id = Guid.NewGuid(),
            ProductId = request.ProductId,
            ChannelId = request.ChannelId,
            CostTypeId = request.CostTypeId,
            Amount = request.Amount,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _repository.Add(cost);
        await _repository.SaveChangesAsync(cancellationToken);

        return new UpsertProductChannelCostResponse { Id = cost.Id, Created = true };
    }
}
