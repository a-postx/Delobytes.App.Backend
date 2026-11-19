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
}

/// <summary>
/// One P&amp;L line as the read model sees it.
/// </summary>
public sealed class PnlLineRow
{
    /// <summary>Gets or sets the P&amp;L line identifier; the keyset cursor is built from it.</summary>
    public Guid Id { get; set; }

    public Guid OrderLineId { get; set; }

    public Guid? ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the catalogue product of the line, or null while no cost fact exists or the
    /// channel product is not linked to a product.
    /// </summary>
    public Guid? ProductId { get; set; }

    /// <summary>Gets or sets the channel product Sales linked the line to, when known.</summary>
    public Guid? ChannelProductId { get; set; }

    public string Currency { get; set; } = default!;

    public DateOnly? SaleDate { get; set; }

    public PnlLineState State { get; set; }

    public OrderLineCostStatus? CostStatus { get; set; }

    public decimal? RevenueAmount { get; set; }

    public decimal? CommissionAmount { get; set; }

    public decimal? AcquiringAmount { get; set; }

    public decimal? DeliveryFeeAmount { get; set; }

    public decimal? RefundAmount { get; set; }

    public decimal? CompensationAmount { get; set; }

    public decimal? NetRevenueAmount { get; set; }

    public decimal? ControlTotalAmount { get; set; }

    public decimal? ControlDifferenceAmount { get; set; }

    public decimal? ProductionCostAmount { get; set; }

    public bool? ProductionCostIsComplete { get; set; }
}

/// <summary>
/// One keyset page of P&amp;L lines.
/// </summary>
public sealed class PnlLinePage
{
    public IReadOnlyList<PnlLineRow> Rows { get; set; } = Array.Empty<PnlLineRow>();

    public string? NextToken { get; set; }

    /// <summary>Gets or sets how many lines the currency filter excluded.</summary>
    public int ExcludedByCurrency { get; set; }
}

/// <summary>
/// Diagnostics of a period.
/// </summary>
public sealed class PnlDiagnosticsResult
{
    public int UnattributedEntries { get; set; }

    public int AmbiguousAttributions { get; set; }

    public decimal ControlDifference { get; set; }

    public int LinesWithOpenControlDifference { get; set; }

    /// <summary>
    /// Lines in the period whose catalogue product is unknown: either Catalog has not resolved the
    /// cost yet, or the channel product is not linked to a product. Such a line is still visible in
    /// the line list and in the unlinked summary group.
    /// </summary>
    public int LinesWithoutProductLink { get; set; }

    /// <summary>Sum of <c>RevenueAmount</c> over <see cref="LinesWithoutProductLink"/>.</summary>
    public decimal UnlinkedRevenueAmount { get; set; }
}
