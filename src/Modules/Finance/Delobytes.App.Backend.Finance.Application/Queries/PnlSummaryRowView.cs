using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>One summary row.</summary>
public sealed class PnlSummaryRowView : IPnlSkuLabelTarget
{
    public Guid? ProductId { get; set; }

    /// <summary>
    /// Gets or sets the current SKU of the linked catalogue product. Null when the row is not
    /// grouped by product, is unlinked, or the product no longer resolves.
    /// </summary>
    public string? Sku { get; set; }

    /// <summary>
    /// Gets or sets the rank of the row in a product ranking: 1 is the best row of the requested
    /// sort. Null in an unranked summary.
    /// </summary>
    public int? Rank { get; set; }

    /// <summary>
    /// Gets or sets the channel of the group. Populated only when the summary is grouped by channel;
    /// a product-grouped row leaves it empty and the channel stays a filter.
    /// </summary>
    public Guid? ChannelId { get; set; }

    public bool IsUnlinkedProduct { get; set; }

    /// <summary>
    /// Gets or sets the first day of the period, unclipped: a week starts on its Monday and a month
    /// on its 1st even when the filter starts later, so the label always describes a whole period.
    /// </summary>
    public DateOnly PeriodStart { get; set; }

    /// <summary>
    /// Gets or sets the last day of the period, inclusive, on the same unclipped basis as
    /// <see cref="PeriodStart"/>. A total-period row reports the last day of the requested range.
    /// </summary>
    public DateOnly PeriodEnd { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the period is cut by the requested range: the row
    /// then holds only the days inside the filter while its label describes the whole period. A
    /// day period is never partial; a total-period row is the range itself and is never partial.
    /// </summary>
    public bool IsPartial { get; set; }

    public int Lines { get; set; }

    public decimal? RevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed commission the channel withheld over the lines of the group: negative
    /// when money left the seller. Null when no line of the group reported one.
    /// </summary>
    public decimal? CommissionAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed acquiring fee of the group. Null when no line reported one.
    /// </summary>
    public decimal? AcquiringAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed delivery and logistics charge of the group. Null when no line
    /// reported one.
    /// </summary>
    public decimal? DeliveryFeeAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed refunds of the group: negative when money left the seller. Null when
    /// no line reported one.
    /// </summary>
    public decimal? RefundAmount { get; set; }

    /// <summary>
    /// Gets or sets the signed marketplace-funded discount compensation of the group: income, so
    /// positive when the channel paid the seller back. Null when no line reported one.
    /// </summary>
    public decimal? CompensationAmount { get; set; }

    /// <summary>
    /// Gets or sets the money the sales of the group left before cost of goods, on the same basis as
    /// <see cref="RevenueAmount"/>: the revenue plus every component of the deduction breakdown.
    /// </summary>
    public decimal? NetRevenueAmount { get; set; }

    public decimal? ProductionCostAmount { get; set; }

    public decimal? EstimatedTax { get; set; }

    /// <summary>
    /// Gets or sets the margin before tax: the sum over the lines of the group whose cost is known
    /// and complete. Null when no line of the group has a known margin. A non-null value is still
    /// partial while <see cref="IsMarginComplete"/> is false, so compare it with
    /// <see cref="RevenueWithMarginAmount"/> rather than with <see cref="RevenueAmount"/>.
    /// </summary>
    public decimal? Margin { get; set; }

    /// <summary>
    /// Gets or sets the part of <see cref="Margin"/> earned by the lines that sold something: net
    /// revenue minus the production cost of the sold units. Null when no such line of the group has
    /// a known margin. Together with <see cref="MarginWithoutSaleAmount"/> it adds up to
    /// <see cref="Margin"/>.
    /// </summary>
    public decimal? MarginFromSalesAmount { get; set; }

    /// <summary>
    /// Gets or sets the part of <see cref="Margin"/> carried by the lines with no sale at all: a
    /// non-buyout or a fully returned unit never leaves the seller, so no cost of goods applies and
    /// the result of such a line is its net revenue, which is negative when only fees were charged.
    /// Null when the group has no such line. Reported apart from <see cref="MarginFromSalesAmount"/>
    /// so a negative total is never read as a loss on the sales themselves.
    /// </summary>
    public decimal? MarginWithoutSaleAmount { get; set; }

    /// <summary>
    /// Gets or sets the margin net of the estimated tax: the sum over the lines that have both a
    /// known margin and a tax profile. Null when no line of the group has both.
    /// </summary>
    public decimal? TaxAdjustedMargin { get; set; }

    /// <summary>
    /// Gets or sets the contribution margin net of the estimated tax, as a percentage of
    /// <see cref="NetRevenueWithTaxAdjustedMarginAmount"/>: 40 means 40%. The two describe the same
    /// lines, so they are never mixed. Null while the tax layer does not cover the whole group: a
    /// share of a partially taxed group would pair a numerator and a denominator that belong to
    /// different lines, which is not a conservative estimate but a wrong figure.
    /// </summary>
    public decimal? TaxAdjustedContributionMarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on <see cref="InvestedCostWithTaxAdjustedMarginAmount"/> computed from
    /// <see cref="TaxAdjustedMargin"/>: 25 means 25% of the money tied up in the taxed lines whose
    /// investment is known. Null while that population is not the whole group, for the same reason
    /// as <see cref="TaxAdjustedContributionMarginPercent"/>.
    /// </summary>
    public decimal? TaxAdjustedRoiPercent { get; set; }

    /// <summary>
    /// Gets or sets the contribution margin as a percentage of <see cref="NetRevenueWithMarginAmount"/>:
    /// 40 means 40%. The margin is earned after the channel fees, so the gross sale is not its base.
    /// Null when the margin is unknown or the covered net revenue is not positive.
    /// </summary>
    public decimal? ContributionMarginPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on the production cost of the covered sold units, as a percentage:
    /// 25 means 25% of <see cref="ProductionCostWithMarginAmount"/>, never of
    /// <see cref="ProductionCostAmount"/>. Null when the margin is unknown or the covered cost is
    /// not positive.
    /// </summary>
    public decimal? ReturnOnSoldCostPercent { get; set; }

    /// <summary>
    /// Gets or sets the return on <see cref="InvestedCostAmount"/>: 25 means 25% of the money tied up
    /// in the ordered units. The numerator is the margin of the same lines, so a non-buyout lowers
    /// the percentage instead of dropping out of the base. Null when the margin is unknown or the
    /// covered investment is not positive.
    /// </summary>
    public decimal? RoiPercent { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units the group was ordered with, including the non-buyouts: a
    /// line that never sold still carries the units it was ordered with.
    /// </summary>
    public int QuantityOrdered { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units the group actually sold. A non-buyout adds to
    /// <see cref="QuantityOrdered"/> and to nothing here.
    /// </summary>
    public int QuantitySold { get; set; }

    /// <summary>Gets or sets the number of lines of the group whose margin is known.</summary>
    public int LinesWithMargin { get; set; }

    /// <summary>
    /// Gets or sets the number of lines of the group whose tax-adjusted margin is known: a known
    /// margin and a resolved tax profile. It is the population of <see cref="TaxAdjustedMargin"/>,
    /// so a percentage built on it never mixes lines.
    /// </summary>
    public int LinesWithTaxAdjustedMargin { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose tax-adjusted margin is known, on the same basis as
    /// <see cref="RevenueAmount"/>: the tax base the tax layer actually covers. It never exceeds
    /// <see cref="RevenueWithMarginAmount"/>.
    /// </summary>
    public decimal RevenueWithTaxAdjustedMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the net revenue of the lines whose tax-adjusted margin is known: the base of
    /// <see cref="TaxAdjustedContributionMarginPercent"/>.
    /// </summary>
    public decimal NetRevenueWithTaxAdjustedMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the production cost of the ordered units of the lines whose tax-adjusted margin
    /// and investment are both known: the base of <see cref="TaxAdjustedRoiPercent"/>.
    /// </summary>
    public decimal InvestedCostWithTaxAdjustedMarginAmount { get; set; }

    /// <summary>Gets or sets the number of lines the tax-adjusted invested cost is known for.</summary>
    public int LinesWithTaxAdjustedInvestedCost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known tax-adjusted
    /// margin. Only then does <see cref="TaxAdjustedContributionMarginPercent"/> describe the whole
    /// group, so the query reports the percentage only in that case.
    /// </summary>
    public bool IsTaxAdjustedMarginComplete { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known tax-adjusted
    /// margin and a known investment. Only then does <see cref="TaxAdjustedRoiPercent"/> describe the
    /// whole group.
    /// </summary>
    public bool IsTaxAdjustedRoiComplete { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose margin is known, on the same basis as
    /// <see cref="RevenueAmount"/>: the two coverage parts always add up to the total.
    /// </summary>
    public decimal RevenueWithMarginAmount { get; set; }

    /// <summary>Gets or sets the production cost of the lines whose margin is known.</summary>
    public decimal ProductionCostWithMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the net revenue of the lines whose margin is known: the base of
    /// <see cref="ContributionMarginPercent"/>. On the same basis as <see cref="NetRevenueAmount"/>,
    /// so the two coverage parts always add up to the total.
    /// </summary>
    public decimal NetRevenueWithMarginAmount { get; set; }

    /// <summary>
    /// Gets or sets the net revenue of the lines whose margin is unknown: the part of
    /// <see cref="NetRevenueAmount"/> no margin figure can be attributed to.
    /// </summary>
    public decimal NetRevenueWithoutCostAmount { get; set; }

    /// <summary>
    /// Gets or sets the production cost of the ordered units of the lines whose margin and cost are
    /// both known: the base of <see cref="RoiPercent"/>. It covers the units that never sold, so it
    /// can exceed <see cref="ProductionCostWithMarginAmount"/>.
    /// </summary>
    public decimal InvestedCostAmount { get; set; }

    /// <summary>Gets or sets the number of lines the invested cost is known for.</summary>
    public int LinesWithInvestedCost { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known invested cost.
    /// Only then does <see cref="RoiPercent"/> describe the whole group.
    /// </summary>
    public bool IsRoiComplete { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines whose margin is unknown: no cost fact yet, an
    /// incomplete cost, or no net revenue. This is the part of <see cref="RevenueAmount"/> no
    /// margin figure can be attributed to.
    /// </summary>
    public decimal RevenueWithoutCostAmount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether every line of the group has a known margin. Only
    /// then do <see cref="Margin"/>, <see cref="ContributionMarginPercent"/> and
    /// <see cref="ReturnOnSoldCostPercent"/> describe the whole group.
    /// </summary>
    public bool IsMarginComplete { get; set; }

    public decimal? ControlDifferenceAmount { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the group's provisional lines: the ones still waiting for a
    /// document or already past its window. It is the part of <see cref="RevenueAmount"/> whose
    /// figures are not final yet, so the UI can mark it without querying the states separately.
    /// Only meaningful when the query includes provisional lines.
    /// </summary>
    public decimal ProvisionalRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the number of lines of the group whose expected document is past its window and
    /// still open: the population of <see cref="OverdueRevenueAmount"/>.
    /// </summary>
    public int OverdueLines { get; set; }

    /// <summary>
    /// Gets or sets the overdue part of <see cref="ProvisionalRevenueAmount"/>: the lines whose
    /// expected document is past its window and still open. It never exceeds the provisional amount.
    /// </summary>
    public decimal OverdueRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the revenue of the lines closed with a recorded exception. These figures are
    /// final, but a document is missing behind them, so they stay distinguishable from a complete
    /// line.
    /// </summary>
    public decimal ClosedWithExceptionRevenueAmount { get; set; }

    /// <summary>
    /// Gets or sets the most severe completeness among the lines of the group: overdue beats
    /// provisional, provisional beats a recorded exception, and an exception beats a complete line.
    /// </summary>
    public PnlLineCompleteness CompletenessState { get; set; }
}
