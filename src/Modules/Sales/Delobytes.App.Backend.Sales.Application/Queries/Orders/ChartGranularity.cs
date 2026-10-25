namespace Delobytes.App.Backend.Sales.Application.Queries.Orders;

/// <summary>
/// Bucket size used to roll up the orders chart series.
/// </summary>
public enum ChartGranularity
{
    /// <summary>One bucket per tenant-local calendar day.</summary>
    Day = 1,

    /// <summary>One bucket per ISO week, starting Monday.</summary>
    Week = 2,

    /// <summary>One bucket per calendar month, starting on the 1st.</summary>
    Month = 3,
}
