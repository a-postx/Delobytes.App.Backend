namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostsBatch;

/// <summary>
/// Batch cost calculation result.
/// </summary>
public class GetProductCostsBatchResponse
{
    /// <summary>Gets or sets the date the input versions were resolved against.</summary>
    public DateOnly AsOfDate { get; set; }

    /// <summary>Gets or sets the cost summaries for each requested product.</summary>
    public List<ProductCostSummaryDto> Items { get; set; } = new List<ProductCostSummaryDto>();
}

/// <summary>
/// Lightweight cost summary for a single product (no line-by-line breakdown).
/// </summary>
public class ProductCostSummaryDto
{
    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the cost of material components.</summary>
    public decimal MaterialCost { get; set; }

    /// <summary>Gets or sets the cost of logistics components.</summary>
    public decimal LogisticsCost { get; set; }

    /// <summary>Gets or sets the cost of packaging components.</summary>
    public decimal PackagingCost { get; set; }

    /// <summary>Gets or sets the assembly labour cost.</summary>
    public decimal LaborCost { get; set; }

    /// <summary>Gets or sets the total cost of one unit.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Gets or sets a value indicating whether every input was available on the requested date.</summary>
    public bool IsComplete { get; set; }
}
