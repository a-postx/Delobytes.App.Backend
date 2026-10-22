using MediatR;

namespace Delobytes.App.Backend.Sales.Application.Queries.Orders.GetOrdersChart;

/// <summary>
/// Requests the ordered-units and ordered-amount series for the home page chart widget,
/// bucketed by day, week or month over a tenant-chosen period.
/// </summary>
public class GetOrdersChartQuery : IRequest<GetOrdersChartResponse>
{
    /// <summary>
    /// Gets or sets the first day of the period, as a tenant-local calendar date (inclusive).
    /// Null means "apply the default": the last 7 tenant-local days ending on <see cref="To"/>.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>
    /// Gets or sets the last day of the period, as a tenant-local calendar date (inclusive).
    /// Null means "apply the default": today in the tenant's time zone.
    /// </summary>
    public DateOnly? To { get; set; }

    /// <summary>
    /// Gets or sets the requested bucket size. Null means "apply the default": an adaptive
    /// choice based on the resolved range length (see <see cref="Services.SalesChartBucketing.ResolveAdaptiveGranularity"/>).
    /// An explicit value is honoured as-is, even when it yields a long series.
    /// </summary>
    public ChartGranularity? Granularity { get; set; }

    /// <summary>
    /// Gets or sets the optional sales channel filter (Catalog.Channel.Id). Null means
    /// "all channels" and is the default. No existence check is performed.
    /// </summary>
    public Guid? ChannelId { get; set; }
}
