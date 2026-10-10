using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// One page of normalized orders as returned by a channel API client.
/// </summary>
public class OrdersPage
{
    /// <summary>
    /// Gets the orders in this page.
    /// </summary>
    public IReadOnlyList<ChannelOrderSnapshot> Orders { get; init; } = Array.Empty<ChannelOrderSnapshot>();

    /// <summary>
    /// Gets the cursor to pass to the next call, or null when this is the last page.
    /// </summary>
    public OrdersPageCursor? NextCursor { get; init; }

    /// <summary>
    /// Gets the total number of orders the channel reports for the query, when it reports one.
    /// </summary>
    public int? TotalCount { get; init; }

    /// <summary>
    /// Gets the currency the channel reported at page level, when it reports one.
    /// </summary>
    public string? Currency { get; init; }
}
