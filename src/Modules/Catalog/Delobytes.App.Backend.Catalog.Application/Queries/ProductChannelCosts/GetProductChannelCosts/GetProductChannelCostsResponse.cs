namespace Delobytes.App.Backend.Catalog.Application.Queries.ProductChannelCosts.GetProductChannelCosts;

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
