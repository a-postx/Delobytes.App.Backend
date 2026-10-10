namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// Request for a single page of orders. The interface deliberately has no
/// "give me the whole period" overload: no channel can serve one.
/// </summary>
public class OrdersPageRequest
{
    /// <summary>
    /// Gets or sets the start of the requested period.
    /// </summary>
    public DateTimeOffset From { get; set; }

    /// <summary>
    /// Gets or sets the end of the requested period.
    /// </summary>
    public DateTimeOffset To { get; set; }

    /// <summary>
    /// Gets or sets the cursor for this page. Null requests the first page.
    /// </summary>
    public OrdersPageCursor? Cursor { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of orders to return in this page.
    /// </summary>
    public int Limit { get; set; }

    /// <summary>
    /// Gets or sets an optional prefilter pushed down to the channel, expressed in the
    /// channel's own product identifiers. Null means no filtering was requested.
    /// </summary>
    public IReadOnlyList<string>? ExternalProductIds { get; set; }
}
