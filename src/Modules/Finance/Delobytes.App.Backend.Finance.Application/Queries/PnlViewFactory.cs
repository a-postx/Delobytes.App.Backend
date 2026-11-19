using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.ReadModels;

namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Enriches P&amp;L rows with the tax layer and the derived margins. Shared by the line and the
/// summary queries so both report the same figures for the same row.
/// </summary>
internal static class PnlViewFactory
{
    public static async Task<List<PnlLineView>> BuildAsync(
        IReadOnlyList<PnlLineRow> rows,
        Guid tenantId,
        ITenantSettingsProvider settingsProvider,
        ITenantTimeService timeService,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo timeZone = await timeService.GetTimeZoneAsync(tenantId, cancellationToken);
        Dictionary<DateOnly, TenantTaxProfileSnapshot?> profiles = new();

        List<PnlLineView> views = new(rows.Count);

        foreach (PnlLineRow row in rows)
        {
            TenantTaxProfileSnapshot? profile = null;

            if (row.SaleDate.HasValue)
            {
                if (!profiles.TryGetValue(row.SaleDate.Value, out profile))
                {
                    profile = await settingsProvider.GetTaxProfileAtAsync(
                        tenantId,
                        TenantTimeZones.StartOfDayUtc(row.SaleDate.Value, timeZone),
                        cancellationToken);

                    profiles.Add(row.SaleDate.Value, profile);
                }
            }

            decimal? estimatedVat = profile == null
                ? null
                : PnlTaxCalculator.EstimatedVat(row.RevenueAmount, profile.Vat);

            decimal? estimatedTax = PnlTaxCalculator.EstimatedTax(row.RevenueAmount, profile);
            decimal? fee = PnlTaxCalculator.MarketplaceFee(
                row.CommissionAmount, row.AcquiringAmount, row.DeliveryFeeAmount);

            // Tax is a separate layer: the margin is computed from the sale and the cost alone, and
            // only the optional tax-adjusted figure depends on the tenant's profile.
            decimal? margin = PnlTaxCalculator.Margin(
                row.NetRevenueAmount, row.ProductionCostAmount, row.ProductionCostIsComplete);

            decimal? taxAdjustedMargin = PnlTaxCalculator.TaxAdjustedMargin(margin, estimatedTax);

            views.Add(new PnlLineView
            {
                OrderLineId = row.OrderLineId,
                ChannelId = row.ChannelId,
                ProductId = row.ProductId,
                ChannelProductId = row.ChannelProductId,
                HasProductLink = row.ProductId.HasValue,
                SaleDate = row.SaleDate,
                Currency = row.Currency,
                RevenueAmount = row.RevenueAmount,
                CompensationAmount = row.CompensationAmount,
                NetRevenueAmount = row.NetRevenueAmount,
                ProductionCostAmount = row.ProductionCostAmount,
                EstimatedVat = estimatedVat,
                EstimatedTax = estimatedTax,
                MarketplaceFee = fee,
                Margin = margin,
                TaxAdjustedMargin = taxAdjustedMargin,
                MarginPercent = PnlTaxCalculator.MarginPercent(margin, row.RevenueAmount),
                Roi = PnlTaxCalculator.Roi(margin, row.ProductionCostAmount),
                CostComplete = row.ProductionCostIsComplete == true,
                CostStatus = row.CostStatus?.ToString(),
                ComponentsReconciled =
                    row.ControlDifferenceAmount.HasValue
                    && Math.Abs(row.ControlDifferenceAmount.Value) <= 0.01m,
            });
        }

        return views;
    }
}
