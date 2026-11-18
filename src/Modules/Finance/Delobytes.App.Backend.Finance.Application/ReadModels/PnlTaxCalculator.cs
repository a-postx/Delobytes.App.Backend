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
    /// Computes the margin.
    /// </summary>
    /// <param name="netRevenue">Net revenue.</param>
    /// <param name="productionCost">Production cost.</param>
    /// <param name="estimatedTax">Estimated tax.</param>
    /// <returns>The margin, or null when an input is missing.</returns>
    public static decimal? Margin(decimal? netRevenue, decimal? productionCost, decimal? estimatedTax)
    {
        if (!netRevenue.HasValue || !productionCost.HasValue || !estimatedTax.HasValue)
        {
            return null;
        }

        return netRevenue.Value - productionCost.Value - estimatedTax.Value;
    }

    /// <summary>
    /// Computes the margin as a share of revenue.
    /// </summary>
    /// <param name="margin">Margin.</param>
    /// <param name="revenue">Revenue.</param>
    /// <returns>The share, or null when an input is missing or the denominator is zero.</returns>
    public static decimal? MarginPercent(decimal? margin, decimal? revenue)
    {
        if (!margin.HasValue || !revenue.HasValue || revenue.Value == 0m)
        {
            return null;
        }

        return margin.Value / revenue.Value;
    }

    /// <summary>
    /// Computes the return on the production cost.
    /// </summary>
    /// <param name="margin">Margin.</param>
    /// <param name="productionCost">Production cost.</param>
    /// <returns>The ratio, or null when an input is missing or the denominator is zero.</returns>
    public static decimal? Roi(decimal? margin, decimal? productionCost)
    {
        if (!margin.HasValue || !productionCost.HasValue || productionCost.Value == 0m)
        {
            return null;
        }

        return margin.Value / productionCost.Value;
    }
}
