namespace Delobytes.App.Backend.Sales.Application.Queries.Orders.GetOrdersChart;

/// <summary>
/// The ordered-units and ordered-amount series for the orders chart widget, together with the
/// resolved period, granularity, currency and channel filter that produced it.
/// </summary>
public class GetOrdersChartResponse
{
    /// <summary>Gets or sets the resolved first day of the period (tenant-local, inclusive).</summary>
    public DateOnly From { get; set; }

    /// <summary>Gets or sets the resolved last day of the period (tenant-local, inclusive).</summary>
    public DateOnly To { get; set; }

    /// <summary>Gets or sets the granularity actually used to build <see cref="Points"/>.</summary>
    public ChartGranularity Granularity { get; set; }

    /// <summary>Gets or sets the tenant currency that was applied as a filter.</summary>
    public string Currency { get; set; } = default!;

    /// <summary>Gets or sets the channel filter as received. Null means "all channels".</summary>
    public Guid? ChannelId { get; set; }

    /// <summary>Gets or sets the totals across every point in <see cref="Points"/>.</summary>
    public OrdersChartTotals Totals { get; set; } = new();

    /// <summary>
    /// Gets or sets the series, ascending by <see cref="OrdersChartPoint.PeriodStart"/>,
    /// contiguous and zero-filled. Never empty.
    /// </summary>
    public List<OrdersChartPoint> Points { get; set; } = new();
}

/// <summary>
/// Sum of <see cref="OrdersChartPoint.Quantity"/> and <see cref="OrdersChartPoint.Amount"/>
/// across every point of a <see cref="GetOrdersChartResponse"/>.
/// </summary>
public class OrdersChartTotals
{
    /// <summary>Gets or sets the total ordered units across the period.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the total ordered amount across the period, rounded to 2 decimals.</summary>
    public decimal Amount { get; set; }
}

/// <summary>
/// One bucket of the orders chart series.
/// </summary>
public class OrdersChartPoint
{
    /// <summary>Gets or sets the first day of the bucket. May precede the requested <c>from</c>.</summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>Gets or sets the ordered units in this bucket. Zero when there is no data.</summary>
    public int Quantity { get; set; }

    /// <summary>Gets or sets the ordered amount in this bucket, rounded to 2 decimals. Zero when there is no data.</summary>
    public decimal Amount { get; set; }
}
