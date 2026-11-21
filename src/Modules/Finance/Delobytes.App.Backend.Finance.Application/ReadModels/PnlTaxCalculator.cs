using Delobytes.App.Backend.Contracts.Accounting;

namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// The tax and margin formulas of the read model. Pure, so every VAT mode and every missing input
/// is pinned by a test rather than hidden in a query handler.
/// </summary>
public static class PnlTaxCalculator
{
    /// <summary>
    /// Returns the VAT rate in percent for a VAT mode.
    /// </summary>
    /// <param name="vat">VAT mode.</param>
    /// <returns>The rate in percent.</returns>
    public static decimal VatRate(VatType vat)
    {
        return vat switch
        {
            VatType.None => 0m,
            VatType.Five => 5m,
            VatType.Seven => 7m,
            VatType.TwentyTwo => 22m,
            _ => 0m,
        };
    }

    /// <summary>
    /// Computes the VAT contained in a gross tax base.
    /// </summary>
    /// <param name="taxBase">Tax base, or null when it is unknown.</param>
    /// <param name="vat">VAT mode.</param>
    /// <returns>The VAT amount, or null when the base is unknown.</returns>
    public static decimal? EstimatedVat(decimal? taxBase, VatType vat)
    {
        if (!taxBase.HasValue)
        {
            return null;
        }

        decimal rate = VatRate(vat);

        return taxBase.Value * rate / (100m + rate);
    }

    /// <summary>
    /// Computes the estimated tax from the tax base and the tenant's profile.
    /// </summary>
    /// <param name="taxBase">Tax base, or null when it is unknown.</param>
    /// <param name="profile">Tax profile effective on the sale date, or null when none exists.</param>
    /// <returns>The estimated tax, or null when an input is missing.</returns>
    public static decimal? EstimatedTax(decimal? taxBase, TenantTaxProfileSnapshot? profile)
    {
        if (!taxBase.HasValue || profile == null)
        {
            return null;
        }

        decimal vat = EstimatedVat(taxBase, profile.Vat) ?? 0m;

        return (taxBase.Value - vat) * profile.RatePercent / 100m;
    }

    /// <summary>
    /// Computes what the channel charged: commission, acquiring and logistics with a positive sign
    /// when money left the seller.
    /// </summary>
    /// <param name="commission">Signed commission.</param>
    /// <param name="acquiring">Signed acquiring.</param>
    /// <param name="logistics">Signed logistics.</param>
    /// <returns>The fee, or null when no component was reported.</returns>
    public static decimal? MarketplaceFee(decimal? commission, decimal? acquiring, decimal? logistics)
    {
        if (!commission.HasValue && !acquiring.HasValue && !logistics.HasValue)
        {
            return null;
        }

        return -((commission ?? 0m) + (acquiring ?? 0m) + (logistics ?? 0m));
    }

    /// <summary>
    /// Computes the margin before tax: what the sale left after the production cost. Tax is a
    /// period-level layer, so it is applied by <see cref="TaxAdjustedMargin"/> instead of here.
    /// </summary>
    /// <param name="netRevenue">Net revenue.</param>
    /// <param name="productionCost">Production cost.</param>
    /// <param name="costComplete">
    /// Whether the cost covers every input. Null when no cost fact exists.
    /// </param>
    /// <returns>
    /// The margin, or null when an input is missing or the cost is incomplete: a margin built on a
    /// partial cost is not a conservative estimate, it is an overstated figure.
    /// </returns>
    public static decimal? Margin(decimal? netRevenue, decimal? productionCost, bool? costComplete)
    {
        if (!netRevenue.HasValue || !productionCost.HasValue || costComplete != true)
        {
            return null;
        }

        return netRevenue.Value - productionCost.Value;
    }

    /// <summary>
    /// Computes the margin of one line: the sale-based <see cref="Margin"/> when a unit sold, and the
    /// net revenue itself when it did not. A line that never sold carries no cost of goods by
    /// construction - no unit left the seller - so its result is known even though no cost fact
    /// exists. Without this a non-buyout's delivery charge would stay outside every margin figure.
    /// </summary>
    /// <param name="netRevenue">Net revenue.</param>
    /// <param name="productionCost">Production cost.</param>
    /// <param name="costComplete">
    /// Whether the cost covers every input. Null when no cost fact exists.
    /// </param>
    /// <param name="hasSale">
    /// Whether the line has a sale fact. False means no cost of goods applies at all.
    /// </param>
    /// <returns>The margin, or null when an input the rule needs is missing.</returns>
    public static decimal? LineMargin(
        decimal? netRevenue,
        decimal? productionCost,
        bool? costComplete,
        bool hasSale)
    {
        return hasSale
            ? Margin(netRevenue, productionCost, costComplete)
            : netRevenue;
    }

    /// <summary>
    /// Subtracts the estimated tax from the pre-tax margin. The tax layer is optional: without a
    /// tenant tax profile the pre-tax margin stays available and only this figure is unknown.
    /// </summary>
    /// <param name="margin">Pre-tax margin.</param>
    /// <param name="estimatedTax">Estimated tax, or null when no profile applies.</param>
    /// <returns>The margin net of the estimated tax, or null when an input is missing.</returns>
    public static decimal? TaxAdjustedMargin(decimal? margin, decimal? estimatedTax)
    {
        if (!margin.HasValue || !estimatedTax.HasValue)
        {
            return null;
        }

        return margin.Value - estimatedTax.Value;
    }

    /// <summary>
    /// Computes the contribution margin as a percentage of the net revenue: 5 means 5%, not a share
    /// of 0.05. The margin is earned after the channel fees, so the base is the money those fees left
    /// behind rather than the gross sale.
    /// </summary>
    /// <param name="margin">Margin.</param>
    /// <param name="netRevenue">Net revenue.</param>
    /// <returns>
    /// The percentage, or null when an input is missing or the net revenue is not positive. A
    /// non-positive base would turn a loss into a positive share, so it reports unknown instead.
    /// </returns>
    public static decimal? ContributionMarginPercent(decimal? margin, decimal? netRevenue)
    {
        if (!margin.HasValue || !netRevenue.HasValue || netRevenue.Value <= 0m)
        {
            return null;
        }

        return margin.Value / netRevenue.Value * 100m;
    }

    /// <summary>
    /// Computes the return on the production cost of the sold units: 25 means 25% of that cost.
    /// </summary>
    /// <param name="margin">Margin.</param>
    /// <param name="productionCost">Production cost of the sold units.</param>
    /// <returns>The percentage, or null when an input is missing or the denominator is not positive.</returns>
    public static decimal? ReturnOnSoldCostPercent(decimal? margin, decimal? productionCost)
    {
        if (!margin.HasValue || !productionCost.HasValue || productionCost.Value <= 0m)
        {
            return null;
        }

        return margin.Value / productionCost.Value * 100m;
    }

    /// <summary>
    /// Computes the return on the cost of goods invested in the ordered units: 25 means 25% of that
    /// investment. The margin is earned on the units that stayed sold, while the investment covers
    /// every ordered unit, so a non-buyout or a return lowers the percentage instead of vanishing
    /// from the base.
    /// </summary>
    /// <param name="margin">Margin.</param>
    /// <param name="investedCost">Production cost of the ordered units.</param>
    /// <returns>The percentage, or null when an input is missing or the denominator is not positive.</returns>
    public static decimal? RoiPercent(decimal? margin, decimal? investedCost)
    {
        if (!margin.HasValue || !investedCost.HasValue || investedCost.Value <= 0m)
        {
            return null;
        }

        return margin.Value / investedCost.Value * 100m;
    }

    /// <summary>
    /// Computes the production cost of the ordered units: the investment a line ties up.
    /// </summary>
    /// <remarks>
    /// A cost fact that carries only the unit price is multiplied by the ordered quantity. A fact
    /// that carries only the line total is prorated by its own quantity, because the ordered quantity
    /// of the reference can differ from the quantity the cost was resolved for.
    /// </remarks>
    /// <param name="unitTotalCost">Total cost of one unit, or null when the fact has only a line total.</param>
    /// <param name="totalCost">Total cost of the line the fact was resolved for, or null.</param>
    /// <param name="costQuantity">Quantity the line total was resolved for, or null.</param>
    /// <param name="orderedQuantity">Ordered quantity of the line.</param>
    /// <returns>The invested cost, or null when no amount or no quantity can produce it.</returns>
    public static decimal? InvestedCostOfOrderedUnits(
        decimal? unitTotalCost,
        decimal? totalCost,
        int? costQuantity,
        int orderedQuantity)
    {
        if (orderedQuantity <= 0)
        {
            return null;
        }

        if (unitTotalCost.HasValue)
        {
            return unitTotalCost.Value * orderedQuantity;
        }

        if (totalCost.HasValue && costQuantity is > 0)
        {
            return totalCost.Value * orderedQuantity / costQuantity.Value;
        }

        return null;
    }
}
