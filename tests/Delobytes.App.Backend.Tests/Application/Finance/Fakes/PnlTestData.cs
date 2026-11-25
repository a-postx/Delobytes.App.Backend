using System;
using System.Collections.Generic;
using System.Threading;
using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Moq;

namespace Delobytes.App.Backend.Tests.Application.Finance.Fakes;

/// <summary>
/// Line and handler fixtures shared by the P&amp;L handler suites. One class held five handler suites
/// and these builders; the suites are split by handler now, so the builders live next to the other
/// finance fakes instead of being copied into each suite.
/// </summary>
internal static class PnlTestData
{
    internal static readonly Guid TenantId = Guid.NewGuid();

    internal static GetPnlSummaryQueryHandler SummaryHandler(IReadOnlyList<PnlLineRow> rows)
    {
        return new GetPnlSummaryQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            TenantContext(),
            SettingsProvider().Object, Lookup().Object);
    }

    internal static PnlLineRow Row(
        DateOnly? saleDate,
        decimal? revenue,
        decimal? netRevenue,
        decimal? cost,
        Guid? productId = null,
        bool costComplete = true,
        PnlLineCompleteness completeness = PnlLineCompleteness.Complete,
        DateOnly? periodDate = null,
        int quantity = 1,
        Guid? channelId = null,
        int returnedQuantity = 0,
        decimal? unitCost = null,
        decimal? costTotal = null,
        int? costQuantity = null,
        decimal? commission = null,
        decimal? acquiring = null,
        decimal? deliveryFee = null,
        decimal? refund = null,
        decimal? compensation = null,
        string? externalOrderId = null,
        string? externalLineId = null,
        string? externalProductId = null,
        DateTimeOffset? orderDate = null)
    {
        // The line stores only the cost of the units that stayed sold, so the unit cost is derived
        // from that population unless the caller spells the cost fact out. A caller that passes a
        // line total without a unit cost is testing the proration rule, so the derivation is skipped.
        int soldUnits = Math.Max(0, quantity - returnedQuantity);
        bool hasCostFact = cost.HasValue || unitCost.HasValue || (costTotal.HasValue && costQuantity.HasValue);

        decimal? resolvedUnitCost = unitCost;

        if (!resolvedUnitCost.HasValue && !costTotal.HasValue && cost.HasValue && soldUnits > 0)
        {
            resolvedUnitCost = cost.Value / soldUnits;
        }

        return new PnlLineRow
        {
            OrderLineId = Guid.NewGuid(),
            ExternalOrderId = externalOrderId ?? "order-" + Guid.NewGuid(),
            ExternalLineId = externalLineId,
            ExternalProductId = externalProductId ?? "sku-" + Guid.NewGuid(),
            OrderDate = orderDate ?? new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            ChannelId = channelId ?? Guid.NewGuid(),
            ProductId = productId,
            Currency = "RUB",
            SaleDate = saleDate,
            PeriodDate = periodDate ?? saleDate,
            State = saleDate.HasValue ? PnlLineState.Calculated : PnlLineState.NotCalculated,
            Quantity = quantity,
            ReturnedQuantity = returnedQuantity,
            RevenueAmount = revenue,
            CommissionAmount = commission,
            AcquiringAmount = acquiring,
            DeliveryFeeAmount = deliveryFee,
            RefundAmount = refund,
            CompensationAmount = compensation,
            NetRevenueAmount = netRevenue,
            ProductionCostAmount = cost,
            ControlDifferenceAmount = 0m,
            ProductionCostIsComplete = cost.HasValue ? costComplete : null,
            UnitTotalCost = resolvedUnitCost,
            TotalCost = costTotal ?? cost,
            CostQuantity = costQuantity ?? (cost.HasValue ? quantity : null),
            CostIsComplete = hasCostFact ? costComplete : null,
            CostStatus = OrderLineCostStatus.Resolved,
            CompletenessState = completeness,
        };
    }

    internal static Mock<IProductSkuLookup> Lookup()
    {
        Mock<IProductSkuLookup> lookup = new();

        // An unconfigured Task<IReadOnlyDictionary<...>> mock returns null, which the handlers
        // would dereference. Tests that do not care about labels get an empty, successful result.
        lookup
            .Setup(l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());

        return lookup;
    }

    internal static ITenantContext TenantContext()
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();

        tenant.SetupGet(t => t.TenantId).Returns(TenantId);

        return tenant.Object;
    }

    internal static Mock<ITenantSettingsProvider> SettingsProvider()
    {
        return SettingsProvider("UTC");
    }

    internal static Mock<ITenantSettingsProvider> SettingsProvider(string timeZoneId)
    {
        Mock<ITenantSettingsProvider> provider = new Mock<ITenantSettingsProvider>();

        provider
            .Setup(p => p.GetAccountingSettingsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = timeZoneId });

        provider
            .Setup(p => p.GetTaxProfileHistoryAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantTaxProfileSnapshot>
            {
                new()
                {
                    Regime = TaxRegime.UsnIncome,
                    RatePercent = 6m,
                    Vat = VatType.None,
                    ValidFrom = new DateOnly(2026, 1, 1),
                },
            });

        return provider;
    }

    internal static Mock<ITenantSettingsProvider> SettingsProviderWithoutTaxProfile()
    {
        Mock<ITenantSettingsProvider> provider = new Mock<ITenantSettingsProvider>();

        provider
            .Setup(p => p.GetAccountingSettingsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "UTC" });

        provider
            .Setup(p => p.GetTaxProfileHistoryAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantTaxProfileSnapshot>());

        return provider;
    }
}
