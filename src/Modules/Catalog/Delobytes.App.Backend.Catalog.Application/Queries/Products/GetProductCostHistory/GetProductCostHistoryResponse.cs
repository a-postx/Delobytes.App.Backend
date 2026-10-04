namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostHistory;

public class GetProductCostHistoryResponse
{
    public bool Found { get; set; }
    public int TotalCount { get; set; }
    public List<ProductCostSnapshotDto> Items { get; set; } = new List<ProductCostSnapshotDto>();
}

public class ProductCostSnapshotDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public DateOnly AsOfDate { get; set; }
    public decimal MaterialCost { get; set; }
    public decimal LogisticsCost { get; set; }
    public decimal PackagingCost { get; set; }
    public decimal LaborCost { get; set; }
    public decimal TotalCost { get; set; }
    public bool IsComplete { get; set; }
    public string LinesSnapshotJson { get; set; } = default!;
    public string TriggerReason { get; set; } = default!;
    public DateTimeOffset CalculatedAt { get; set; }
}
