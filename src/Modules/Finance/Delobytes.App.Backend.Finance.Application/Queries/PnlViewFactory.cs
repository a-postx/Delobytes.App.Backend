using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;

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
        CancellationToken cancellationToken)
    {
        // The history is read once per request: a report over years of history asks for one profile
        // per distinct sale date, and one query per date was the second half of the timeout.
        IReadOnlyList<TenantTaxProfileSnapshot> profiles =
            await settingsProvider.GetTaxProfileHistoryAsync(tenantId, cancellationToken);

        TaxProfileTimeline timeline = new(profiles);

        List<PnlLineView> views = new(rows.Count);

        foreach (PnlLineRow row in rows)
        {
            // The sale date is already the tenant's calendar date, so the profile is resolved on it
            // directly: converting it to UTC and back would return the same date.
            TenantTaxProfileSnapshot? profile = row.SaleDate.HasValue
                ? timeline.Resolve(row.SaleDate.Value)
                : null;

            decimal? estimatedVat = profile == null
                ? null
                : PnlTaxCalculator.EstimatedVat(row.RevenueAmount, profile.Vat);

            decimal? estimatedTax = PnlTaxCalculator.EstimatedTax(row.RevenueAmount, profile);
            decimal? fee = PnlTaxCalculator.MarketplaceFee(
                row.CommissionAmount, row.AcquiringAmount, row.DeliveryFeeAmount);

            // Tax is a separate layer: the margin is computed from the sale and the cost alone, and
            // only the optional tax-adjusted figure depends on the tenant's profile. A line without
            // a sale has no cost of goods, so its result is the net revenue itself.
            decimal? margin = PnlTaxCalculator.LineMargin(
                row.NetRevenueAmount,
                row.ProductionCostAmount,
                row.ProductionCostIsComplete,
                row.SaleDate.HasValue);

            decimal? taxAdjustedMargin = PnlTaxCalculator.TaxAdjustedMargin(margin, estimatedTax);

            views.Add(new PnlLineView
            {
                OrderLineId = row.OrderLineId,
                ChannelId = row.ChannelId,
                ProductId = row.ProductId,
                ChannelProductId = row.ChannelProductId,
                HasProductLink = row.ProductId.HasValue,
                SaleDate = row.SaleDate,
                PeriodDate = row.PeriodDate,
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
                CompletenessState = row.CompletenessState,
                MissingDocuments = row.MissingDocumentTypes.ToList(),
                ExpectedBy = row.ExpectedBy,

                // A figure is provisional while its documents are still expected or already late.
                // Closed with an exception is final: the gap was accepted and recorded.
                IsProvisional = PnlLineCompletenessSemantics.IsProvisional(row.CompletenessState),
            });
        }

        return views;
    }
}
