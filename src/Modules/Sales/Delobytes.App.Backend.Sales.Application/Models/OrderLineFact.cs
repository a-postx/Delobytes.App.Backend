namespace Delobytes.App.Backend.Sales.Application.Models;

/// <summary>
/// A flat, per-line fact of a surviving order line, already filtered by currency and
/// cancellation. Carries no tenant or identifier columns: the repository query that produces
/// it has already scoped everything that matters.
/// </summary>
public class OrderLineFact
{
    /// <summary>Gets or sets the owning order's placement time, stored and returned as UTC.</summary>
    public DateTimeOffset OrderDate { get; set; }

    /// <summary>Gets or sets the line quantity.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the seller's unit price after the seller's own discount. Null contributes 0 to amount.</summary>
    public decimal? UnitPrice { get; set; }
}
