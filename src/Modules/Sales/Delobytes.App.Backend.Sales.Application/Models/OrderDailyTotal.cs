namespace Delobytes.App.Backend.Sales.Application.Models;

/// <summary>
/// One tenant-local calendar day after grouping <see cref="OrderLineFact"/> rows. The unit the
/// bucketing step rolls up into day/week/month buckets.
/// </summary>
public class OrderDailyTotal
{
    /// <summary>Gets or sets the tenant-local calendar day.</summary>
    public DateOnly Day { get; set; }

    /// <summary>Gets or sets the ordered units on this day.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the ordered amount on this day.</summary>
    public decimal Amount { get; set; }
}
