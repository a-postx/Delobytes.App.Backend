using Delobytes.App.Backend.Finance.Application.ReadModels;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>One P&amp;L line returned by the read model.</summary>
public sealed class PnlLineView : IPnlSkuLabelTarget
{
    public Guid OrderLineId { get; set; }

    /// <summary>Gets or sets the marketplace order identity, as Sales reported it.</summary>
    public string ExternalOrderId { get; set; } = default!;

    /// <summary>Gets or sets the marketplace unit identity, or null when the channel reports none.</summary>
    public string? ExternalLineId { get; set; }

    /// <summary>Gets or sets the marketplace product identity.</summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the order placement date in the tenant's calendar. Null when the view is built
    /// for aggregate folding (the summary), which never consumes it.
    /// </summary>
    public DateOnly? OrderDate { get; set; }

    public Guid? ChannelId { get; set; }

    public Guid? ProductId { get; set; }

    /// <summary>
    /// Gets or sets the current SKU of the linked catalogue product. Null when the line has no
    /// product link or the product no longer resolves: the identifier stays the source of truth,
    /// the label is display data only.
    /// </summary>
    public string? Sku { get; set; }

    /// <summary>Gets or sets the channel product Sales linked the line to, when known.</summary>
    public Guid? ChannelProductId { get; set; }

    public bool HasProductLink { get; set; }

    public DateOnly? SaleDate { get; set; }

    /// <summary>
    /// Gets or sets the date the line is reported under: the sale date when it sold, otherwise the
    /// first day of its financial period. Null only when the line has neither.
    /// </summary>
    public DateOnly? PeriodDate { get; set; }

    public string Currency { get; set; } = default!;

    public decimal? RevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed commission the channel withheld: negative when money left the seller.
    /// Null when no commission entry arrived at all, which is not the same as a reported zero.
    /// </summary>
    public decimal? CommissionAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed acquiring fee: negative when money left the seller. Null when the
    /// channel reported none.
    /// </summary>
    public decimal? AcquiringAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed delivery and logistics charge: negative when money left the seller.
    /// Null when the channel reported none.
    /// </summary>
    public decimal? DeliveryFeeAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed refund of a returned unit: negative when money left the seller. It is
    /// a separate line of the breakdown and is already part of <see cref="NetRevenueAmount"/>.
    /// </summary>
    public decimal? RefundAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed marketplace-funded discount compensation: income, so positive when
    /// the channel paid the seller back. Null when no compensation entry arrived.
    /// </summary>
    public decimal? CompensationAmount { get; set; }

    /// <summary>
    /// Gets or sets the money the sale left before cost of goods: the sale plus every deduction of
    /// the breakdown. The components always add up to it.
    /// </summary>
    public decimal? NetRevenueAmount { get; set; }

    public decimal? ProductionCostAmount { get; set; }

    public decimal? EstimatedVat { get; set; }

    public decimal? EstimatedTax { get; set; }

    /// <summary>
    /// Gets or sets the channel fee with a positive sign: the commission, the acquiring fee and the
    /// delivery charge together. It is a convenience total for the older callers; the breakdown is
    /// reported by the signed component amounts, which are the source of truth.
    /// </summary>
    public decimal? MarketplaceFee { get; set; }

    /// <summary>
    /// Gets or sets the margin before tax: net revenue minus the production cost. Null while the
    /// cost is unknown or incomplete, and never dependent on the tenant's tax settings.
    /// </summary>
    public decimal? Margin { get; set; }

    /// <summary>
    /// Gets or sets the margin net of the estimated tax. Null when the pre-tax margin or the tax
    /// profile is unknown: the tax layer is optional and does not gate the pre-tax figure.
    /// </summary>
    public decimal? TaxAdjustedMargin { get; set; }

    /// <summary>
    /// Gets or sets the contribution margin as a percentage of <see cref="NetRevenueAmount"/>: 5
    /// means 5%. The margin is earned after the channel fees, so the gross sale is not its base.
    /// Null when the margin is unknown or the net revenue is not positive.
    /// </summary>
    public decimal? ContributionMarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the contribution margin net of the estimated tax, as a percentage of the same
    /// <see cref="NetRevenueAmount"/>. The numerator and the denominator are both this line, so no
    /// coverage rule applies. Null when <see cref="TaxAdjustedMargin"/> is unknown or the net
    /// revenue is not positive.
    /// </summary>
    public decimal? TaxAdjustedContributionMarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on the production cost of the sold units: 25 means 25% of
    /// <see cref="ProductionCostAmount"/>. Null when the margin is unknown or the cost is not
    /// positive.
    /// </summary>
    public decimal? ReturnOnSoldCostPercent { get; set; }

    /// <summary>
    /// Gets or sets the production cost of the ordered units: the investment the line ties up. It
    /// covers the units that never sold, so it is known even when no cost of goods is charged.
    /// Null when no complete cost fact exists.
    /// </summary>
    public decimal? InvestedCostAmount { get; set; }

    /// <summary>
    /// Gets or sets the return on <see cref="InvestedCostAmount"/>: 25 means 25% of the money tied up
    /// in the ordered units. Null when the margin or the investment is unknown or the investment is
    /// not positive.
    /// </summary>
    public decimal? RoiPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on <see cref="InvestedCostAmount"/> computed from
    /// <see cref="TaxAdjustedMargin"/>. The numerator and the denominator are both this line, so no
    /// coverage rule applies. Null when the tax-adjusted margin or the investment is unknown or the
    /// investment is not positive.
    /// </summary>
    public decimal? TaxAdjustedRoiPercent { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units the line was ordered with. A line that never sold still
    /// carries the units it was ordered with, so this figure includes the non-buyouts.
    /// </summary>
    public int QuantityOrdered { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units that actually sold: the ordered quantity of a line that
    /// has a sale fact, and zero for a line that never sold.
    /// </summary>
    public int QuantitySold { get; set; }

    public bool CostComplete { get; set; }

    public string? CostStatus { get; set; }

    public bool ComponentsReconciled { get; set; }

    /// <summary>Gets or sets whether every expected document of the sale has arrived.</summary>
    public Domain.Enums.PnlLineCompleteness CompletenessState { get; set; }

    /// <summary>Gets or sets the document types still expected, by name.</summary>
    public List<string> MissingDocuments { get; set; } = new List<string>();

    /// <summary>Gets or sets the earliest deadline among the open expectations, or null.</summary>
    public DateTimeOffset? ExpectedBy { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the figures are provisional because a document is
    /// still missing. A provisional line must not be reported as final.
    /// </summary>
    public bool IsProvisional { get; set; }
}
