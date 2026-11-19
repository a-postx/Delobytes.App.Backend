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
    /// Gets or sets the optional channel product filter. It matches the link Sales captured, so a
    /// sale whose catalogue product is not resolved yet can still be read.
    /// </summary>
    public Guid? ChannelProductId { get; set; }

    /// <summary>Gets or sets the optional first sale date, inclusive.</summary>
    public DateOnly? From { get; set; }

    /// <summary>Gets or sets the optional last sale date, inclusive.</summary>
    public DateOnly? To { get; set; }

    /// <summary>Gets or sets the optional state filter.</summary>
    public PnlLineState? State { get; set; }

    /// <summary>
    /// Gets or sets the optional completeness filter. Reports pass
    /// <see cref="PnlLineCompleteness.Complete"/> so provisional figures never blend into a KPI;
    /// the operational queue passes the open states.
    /// </summary>
    public PnlLineCompleteness? Completeness { get; set; }
}
