using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Channel-neutral page of orders returned by <see cref="Services.MarketplaceClients.IOrdersSource"/>.
/// </summary>
public class OrdersPage
{
    /// <summary>
    /// Gets or sets the orders retrieved for the requested window.
    /// </summary>
    public List<ChannelOrderSnapshot> Orders { get; set; } = new();

    /// <summary>
    /// Gets or sets a value indicating whether the source has more orders beyond this page.
    /// </summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Gets or sets the number of rows the source could not identify and therefore did not map.
    /// Non-zero means the page is incomplete: whatever consumed it must not treat the run as a
    /// clean success, because an order it never saw cannot be recovered from the snapshot list.
    /// </summary>
    public int RowsWithoutIdentity { get; set; }
}
