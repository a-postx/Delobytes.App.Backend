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
    /// Gets or sets a value indicating whether the response hit the source's row cap and may
    /// therefore cover only part of the requested window. Where it reached is reported separately
    /// by <see cref="CoveredUntil"/>: the rows beyond that instant are unknown, not absent.
    /// </summary>
    public bool HasMore { get; set; }

    /// <summary>
    /// Gets or sets how far into the requested window this response actually reached: the latest
    /// selection instant among the rows the source returned, where the selection instant is the
    /// field the source filters by (<c>lastChangeDate</c> for a delta run, <c>date</c> for a
    /// backfill). Rows outside the requested window count, because they are what consumed the cap.
    /// Equals the window end when the response was complete; null when it was capped and no returned
    /// row carried a parseable instant, which leaves the coverage undeterminable rather than known.
    /// </summary>
    public DateTimeOffset? CoveredUntil { get; set; }

    /// <summary>
    /// Gets or sets the number of rows the source could not identify and therefore did not map.
    /// Non-zero means the page is incomplete: whatever consumed it must not treat the run as a
    /// clean success, because an order it never saw cannot be recovered from the snapshot list.
    /// </summary>
    public int RowsWithoutIdentity { get; set; }
}
