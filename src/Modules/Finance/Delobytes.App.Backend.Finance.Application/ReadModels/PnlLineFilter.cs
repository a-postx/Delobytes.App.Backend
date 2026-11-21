using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Filters of the P&amp;L read queries.
/// </summary>
public sealed class PnlLineFilter
{
    /// <summary>Gets or sets the optional channel filter.</summary>
    public Guid? ChannelId { get; set; }

    /// <summary>Gets or sets the optional product filter.</summary>
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Gets or sets the optional multi-product filter. Null or empty means no restriction; the
    /// single <see cref="ProductId"/> stays the shortcut for one product.
    /// </summary>
    public IReadOnlyList<Guid>? ProductIds { get; set; }

    /// <summary>
    /// Gets or sets the optional channel product filter. It matches the link Sales captured, so a
    /// sale whose catalogue product is not resolved yet can still be read.
    /// </summary>
    public Guid? ChannelProductId { get; set; }

    /// <summary>
    /// Gets or sets the optional first period date, inclusive. The period date is the sale date when
    /// the line sold and the first day of its financial period otherwise, so a non-buyout carrying
    /// only its delivery charge is still selected.
    /// </summary>
    public DateOnly? From { get; set; }

    /// <summary>Gets or sets the optional last period date, inclusive.</summary>
    public DateOnly? To { get; set; }

    /// <summary>Gets or sets the optional state filter.</summary>
    public PnlLineState? State { get; set; }

    /// <summary>
    /// Gets or sets the completeness states to include. Null means no restriction, which the read
    /// query requests as <c>all</c>. Reports default to the final states, so provisional figures
    /// never blend into a KPI while a recorded exception stays visible.
    /// </summary>
    public IReadOnlyList<PnlLineCompleteness>? CompletenessStates { get; set; }
}
