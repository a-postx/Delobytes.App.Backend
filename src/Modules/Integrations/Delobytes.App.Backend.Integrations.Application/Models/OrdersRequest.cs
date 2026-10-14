namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Channel-neutral request for one page of orders, used by <see cref="Services.MarketplaceClients.IOrdersSource"/>.
/// </summary>
public class OrdersRequest
{
    /// <summary>
    /// Gets or sets the start of the requested window (UTC).
    /// </summary>
    public DateTimeOffset DateFrom { get; set; }

    /// <summary>
    /// Gets or sets the end of the requested window (UTC).
    /// </summary>
    public DateTimeOffset DateTo { get; set; }

    /// <summary>
    /// Gets or sets the Wildberries-style selection mode: 0 selects orders by last change date
    /// (delta sync), 1 selects orders by order date (backfill/reconciliation).
    /// </summary>
    public int Flag { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of orders to return.
    /// </summary>
    public int Limit { get; set; }
}
