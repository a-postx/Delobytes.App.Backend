using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;
using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Commands.ProductChannelCosts.CreateProductChannelCost;

public class CreateProductChannelCostCommandHandler : IRequestHandler<CreateProductChannelCostCommand, CreateProductChannelCostResponse>
{
    private readonly IProductChannelCostRepository _repository;

    public CreateProductChannelCostCommandHandler(IProductChannelCostRepository repository)
    {
        _repository = repository;
    }

    public async Task<CreateProductChannelCostResponse> Handle(CreateProductChannelCostCommand request, CancellationToken cancellationToken)
    {
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

        return new CreateProductChannelCostResponse { Id = cost.Id };
    }
}
