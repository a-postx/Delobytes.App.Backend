using Delobytes.App.Backend.Catalog.Domain.Enums;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;

/// <summary>
/// Cost breakdown of a product, shaped for the API surface.
/// </summary>
public class GetProductCostResponse
{
    /// <summary>Gets or sets a value indicating whether the product exists.</summary>
    public bool Found { get; set; }

    /// <summary>Gets or sets the product identifier.</summary>
    public Guid ProductId { get; set; }

    /// <summary>Gets or sets the date the input versions were resolved against.</summary>
    public DateOnly AsOfDate { get; set; }

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

    /// <summary>Gets or sets the detailed BOM lines.</summary>
    public List<GetProductCostLineDto> Lines { get; set; } = new List<GetProductCostLineDto>();

    /// <summary>Gets or sets the reasons the breakdown is incomplete.</summary>
    public List<GetProductCostWarningDto> Warnings { get; set; } = new List<GetProductCostWarningDto>();
}

/// <summary>
/// One BOM position of the cost breakdown.
/// </summary>
public class GetProductCostLineDto
{
    /// <summary>Gets or sets the component identifier.</summary>
    public Guid ComponentId { get; set; }

    /// <summary>Gets or sets the component display name.</summary>
    public string ComponentName { get; set; } = default!;

    /// <summary>Gets or sets the component category that decides the cost bucket.</summary>
    public ComponentCategory Category { get; set; }

    /// <summary>Gets or sets the quantity per one unit of the product.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Gets or sets the price effective on the requested date.</summary>
    public decimal PricePerUnit { get; set; }

    /// <summary>Gets or sets the line total.</summary>
    public decimal LineTotal { get; set; }
}

/// <summary>
/// One reason the cost breakdown is incomplete.
/// </summary>
public class GetProductCostWarningDto
{
    /// <summary>Gets or sets the machine-readable warning kind.</summary>
    public string Type { get; set; } = default!;

    /// <summary>Gets or sets the human-readable message.</summary>
    public string Message { get; set; } = default!;

    /// <summary>Gets or sets the component the warning belongs to, if any.</summary>
    public Guid? ComponentId { get; set; }
}
