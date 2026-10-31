namespace Delobytes.App.Backend.Integrations.Application.Models;

/// <summary>
/// Channel-neutral request for one page of orders, used by <see cref="Interfaces.IOrdersSource"/>.
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
    /// Gets or sets the channel-neutral selection mode. Wildberries maps this to its own
    /// <c>flag</c> query parameter; other channels may ignore it.
    /// </summary>
    public OrdersSelectionMode Selection { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of orders to return.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets an opaque pagination cursor from a previous <see cref="OrdersPage"/>.
    /// Channel-specific; may be ignored by channels that paginate by date window alone.
    /// </summary>
    public string? Cursor { get; set; }

    /// <summary>
    /// Gets or sets the set of channel-specific status codes to filter by.
    /// Channel-specific; may be ignored by channels with no server-side status filter.
    /// </summary>
    public IReadOnlyList<string>? Statuses { get; set; }

    /// <summary>
    /// Gets or sets the channel-specific order schema (e.g. FBO vs FBS) to request.
    /// Channel-specific; may be ignored by channels with a single schema.
    /// </summary>
    public OrdersSchema? Schema { get; set; }
}
