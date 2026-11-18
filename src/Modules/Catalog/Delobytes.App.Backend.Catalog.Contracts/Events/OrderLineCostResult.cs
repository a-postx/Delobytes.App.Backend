namespace Delobytes.App.Backend.Catalog.Contracts.Events;

/// <summary>
/// Whether Catalog could resolve a product for an order line and calculate its cost.
/// </summary>
public enum OrderLineCostStatus
{
    /// <summary>A product was resolved and its cost calculated.</summary>
    Resolved = 0,

    /// <summary>The line has no catalogue link yet, so no cost can be calculated.</summary>
    ProductNotLinked = 1,
}

/// <summary>
/// The production cost Catalog resolved for one order line, as of the tenant-local order date.
/// </summary>
public record OrderLineCostResult
{
    /// <summary>Gets the order line identifier.</summary>
    public Guid OrderLineId { get; init; }

    /// <summary>Gets whether a product was resolved.</summary>
    public OrderLineCostStatus Status { get; init; }

    /// <summary>Gets the resolved catalogue product, or null when no link exists.</summary>
    public Guid? ProductId { get; init; }

    /// <summary>Gets the date the cost inputs were resolved against.</summary>
    public DateOnly AsOfDate { get; init; }

    /// <summary>Gets the line quantity the unit costs were multiplied by.</summary>
    public int Quantity { get; init; }

    /// <summary>Gets the material cost of one unit.</summary>
    public decimal? UnitMaterialCost { get; init; }

    /// <summary>Gets the logistics cost of one unit.</summary>
    public decimal? UnitLogisticsCost { get; init; }

    /// <summary>Gets the packaging cost of one unit.</summary>
    public decimal? UnitPackagingCost { get; init; }

    /// <summary>Gets the labour cost of one unit.</summary>
    public decimal? UnitLaborCost { get; init; }

    /// <summary>Gets the total cost of one unit.</summary>
    public decimal? UnitTotalCost { get; init; }

    /// <summary>Gets the total cost of the whole line.</summary>
    public decimal? TotalCost { get; init; }

    /// <summary>Gets whether every cost input was available on the as-of date.</summary>
    public bool IsComplete { get; init; }

    /// <summary>Gets the warnings that made the calculation incomplete, by their names.</summary>
    public List<string> WarningTypes { get; init; } = new List<string>();

    /// <summary>Gets the instant the result was resolved.</summary>
    public DateTimeOffset ResolvedAt { get; init; }
}
