using MediatR;
using Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;
using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductChannelCosts;

public class GetProductChannelCostsQuery : IRequest<GetProductChannelCostsResponse>
{
    public Guid ProductId { get; set; }

    public Guid? ChannelId { get; set; }
}

public class GetProductChannelCostsResponse
{
    public IReadOnlyList<ProductChannelCostItem> Items { get; set; } = new List<ProductChannelCostItem>();
}

public class ProductChannelCostItem
{
    public Guid Id { get; set; }

    public Guid ProductId { get; set; }

    public Guid ChannelId { get; set; }

    public Guid CostTypeId { get; set; }

    public string CostTypeName { get; set; } = default!;

    public decimal Amount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

public class GetProductChannelCostsQueryHandler
    : IRequestHandler<GetProductChannelCostsQuery, GetProductChannelCostsResponse>
{
    private readonly IProductChannelCostRepository _repository;

    public GetProductChannelCostsQueryHandler(IProductChannelCostRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetProductChannelCostsResponse> Handle(
        GetProductChannelCostsQuery request,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ProductChannelCost> costs = request.ChannelId.HasValue
            ? await _repository.GetByProductAndChannelAsync(request.ProductId, request.ChannelId.Value, cancellationToken)
            : await _repository.GetByProductAsync(request.ProductId, cancellationToken);

        return new GetProductChannelCostsResponse
        {
            Items = costs.Select(c => new ProductChannelCostItem
            {
                Id = c.Id,
                ProductId = c.ProductId,
                ChannelId = c.ChannelId,
                CostTypeId = c.CostTypeId,
                CostTypeName = c.CostType.Name,
                Amount = c.Amount,
                CreatedAt = c.CreatedAt,
                UpdatedAt = c.UpdatedAt,
            }).ToList(),
        };
    }
}
