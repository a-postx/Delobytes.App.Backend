using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the overview handler: one fold serves every dimension of the page, the comparison
/// range is a second fold of its own, and a remark never counts a population the totals do not hold.
/// </summary>
public class GetPnlOverviewQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ChannelOne = Guid.NewGuid();
    private static readonly Guid ProductA = Guid.NewGuid();
    private static readonly Guid ProductB = Guid.NewGuid();

    [Fact]
    public async Task GetOverview_ReportsEveryDimensionFromOneFold()
    {
        PnlReadStoreFake store = new PnlReadStoreFake(BuildRows()) { AppliesPeriodFilter = true };

        PnlOverviewResponse response = await BuildHandler(store).Handle(
            new GetPnlOverviewQuery { Filter = BuildFilter() },
            CancellationToken.None);

        // The period is read once: totals, channels and products are different views of the same
        // snapshot, not three queries that may see different imports.
        store.StreamCount.Should().Be(1);

        response.Totals!.RevenueAmount.Should().Be(2000m);
        response.Totals.Lines.Should().Be(4);
        response.Channels.Should().ContainSingle().Which.ChannelId.Should().Be(ChannelOne);
        response.Currency.Should().Be("RUB");

        // Two products and one unlinked group: the overview links to the product rows, so it
        // reports how many of them exist instead of carrying the rows themselves.
        response.ProductCount.Should().Be(2);
    }

    [Fact]
    public async Task GetOverview_ComparisonRange_IsASecondFoldOfItsOwn()
    {
        PnlReadStoreFake store = new PnlReadStoreFake(BuildRows()) { AppliesPeriodFilter = true };

        await BuildHandler(store).Handle(
            new GetPnlOverviewQuery
            {
                Filter = BuildFilter(),
                ComparisonFrom = new DateOnly(2026, 7, 1),
                ComparisonTo = new DateOnly(2026, 7, 31),
            },
            CancellationToken.None);

        // The comparison covers a different range, so it cannot be derived from the first fold.
        store.StreamCount.Should().Be(2);
        store.LastFilter!.From.Should().Be(new DateOnly(2026, 7, 1));
    }

    [Fact]
    public async Task GetOverview_Attention_CountsOnlyThePopulationsTheTotalsHold()
    {
        PnlReadStoreFake store = new PnlReadStoreFake(BuildRows()) { AppliesPeriodFilter = true };

        PnlOverviewResponse response = await BuildHandler(store).Handle(
            new GetPnlOverviewQuery { Filter = BuildFilter() },
            CancellationToken.None);

        response.Totals!.OverdueLines.Should().Be(1);
        response.Totals.OverdueRevenueAmount.Should().Be(200m);

        response.Attention.Should().Contain(item =>
            item.Code == PnlOverviewAttentionCode.Overdue
            && item.Count == 1
            && item.Amount == 200m);

        // One product sells without a cost fact: its net revenue is unknown to the margin.
        response.Attention.Should().Contain(item =>
            item.Code == PnlOverviewAttentionCode.WithoutCost
            && item.Count == 1
            && item.Amount == 400m);

        // The line without a catalogue product is revenue the page shows but cannot name.
        response.Attention.Should().Contain(item =>
            item.Code == PnlOverviewAttentionCode.Unlinked
            && item.Count == 1
            && item.Amount == 300m);

        response.Attention.Should().NotContain(item => item.Code == PnlOverviewAttentionCode.Loss);
        response.Attention.Should().NotContain(item => item.Code == PnlOverviewAttentionCode.Currency);
    }

    [Fact]
    public async Task GetOverview_MarginDelta_StaysHiddenWhileTheCoverageIsTooLow()
    {
        // The comparison covers the whole revenue while the period covers a tenth of it: the two
        // margins describe different populations, so their change is not a change of one figure.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 7, 5), revenue: 1000m, netRevenue: 900m, cost: 500m, ChannelOne),
            Row(new DateOnly(2026, 8, 5), revenue: 1000m, netRevenue: 900m, cost: 500m, ChannelOne),
            Row(new DateOnly(2026, 8, 6), revenue: 9000m, netRevenue: 8000m, cost: null, ChannelOne),
        };

        PnlOverviewResponse response = await BuildHandler(new PnlReadStoreFake(rows) { AppliesPeriodFilter = true }).Handle(
            new GetPnlOverviewQuery
            {
                Filter = BuildFilter(),
                ComparisonFrom = new DateOnly(2026, 7, 1),
                ComparisonTo = new DateOnly(2026, 7, 31),
            },
            CancellationToken.None);

        response.Margin.CoveragePercent.Should().Be(10m);
        response.Margin.DeltaPercent.Should().BeNull();
        response.Margin.IsComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetOverview_Channels_AreOrderedByRevenueAndShareTheChannelRevenue()
    {
        Guid channelTwo = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), revenue: 300m, netRevenue: 250m, cost: 150m, channelTwo),
            Row(new DateOnly(2026, 8, 1), revenue: 700m, netRevenue: 600m, cost: 400m, ChannelOne),
        };

        PnlOverviewResponse response = await BuildHandler(new PnlReadStoreFake(rows) { AppliesPeriodFilter = true }).Handle(
            new GetPnlOverviewQuery { Filter = BuildFilter() },
            CancellationToken.None);

        response.Channels.Should().HaveCount(2);
        response.Channels[0].ChannelId.Should().Be(ChannelOne);
        response.Channels[0].SharePercent.Should().Be(70m);
        response.Channels[0].DeductionsAmount.Should().Be(100m);
        response.Channels[0].DeductionsPercent.Should().Be(14.3m);
        response.Channels[1].SharePercent.Should().Be(30m);
    }

    [Fact]
    public async Task GetOverview_Buyout_IsMeasuredInPercentagePoints()
    {
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), revenue: 1000m, netRevenue: 900m, cost: 500m, ChannelOne, quantity: 4),
            Row(
                new DateOnly(2026, 8, 2),
                revenue: 500m,
                netRevenue: 400m,
                cost: 200m,
                ChannelOne,
                quantity: 2,
                returnedQuantity: 1),
        };

        PnlOverviewResponse response = await BuildHandler(new PnlReadStoreFake(rows) { AppliesPeriodFilter = true }).Handle(
            new GetPnlOverviewQuery { Filter = BuildFilter() },
            CancellationToken.None);

        PnlOverviewKpiView buyout = response.Kpis
            .Should().ContainSingle(kpi => kpi.Key == PnlOverviewKpiKey.Buyout).Subject;

        // Six units ordered, five sold: the share is a percentage, so its movement is in points.
        buyout.Value.Should().BeApproximately(5m / 6m * 100m, 0.0001m);
        buyout.PreviousValue.Should().BeNull();
        buyout.DeltaPoints.Should().BeNull();
        buyout.DeltaPercent.Should().BeNull();
    }

    private static GetPnlOverviewQueryHandler BuildHandler(PnlReadStoreFake store)
    {
        return new GetPnlOverviewQueryHandler(
            store,
            BuildTenantContext(),
            BuildSettingsProvider().Object);
    }

    private static PnlQueryFilter BuildFilter()
    {
        return new PnlQueryFilter
        {
            From = new DateOnly(2026, 8, 1),
            To = new DateOnly(2026, 8, 31),
        };
    }

    private static List<PnlLineRow> BuildRows()
    {
        return new List<PnlLineRow>
        {
            Row(new DateOnly(2026, 8, 1), revenue: 1000m, netRevenue: 800m, cost: 500m, ChannelOne, productId: ProductA),
            Row(new DateOnly(2026, 8, 2), revenue: 500m, netRevenue: 400m, cost: null, ChannelOne, productId: ProductB),
            Row(new DateOnly(2026, 8, 3), revenue: 300m, netRevenue: 300m, cost: 250m, ChannelOne),
            Row(
                new DateOnly(2026, 8, 4),
                revenue: 200m,
                netRevenue: 160m,
                cost: 100m,
                ChannelOne,
                productId: ProductA,
                completeness: PnlLineCompleteness.Overdue),
        };
    }

    private static PnlLineRow Row(
        DateOnly? saleDate,
        decimal? revenue,
        decimal? netRevenue,
        decimal? cost,
        Guid? channelId,
        Guid? productId = null,
        bool costComplete = true,
        PnlLineCompleteness completeness = PnlLineCompleteness.Complete,
        int quantity = 1,
        int returnedQuantity = 0)
    {
        int soldUnits = Math.Max(0, quantity - returnedQuantity);
        decimal? unitCost = cost.HasValue && soldUnits > 0 ? cost.Value / soldUnits : null;

        return new PnlLineRow
        {
            OrderLineId = Guid.NewGuid(),
            ExternalOrderId = "order-" + Guid.NewGuid(),
            ExternalProductId = "sku-" + Guid.NewGuid(),
            OrderDate = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.Zero),
            ChannelId = channelId,
            ProductId = productId,
            Currency = "RUB",
            SaleDate = saleDate,
            PeriodDate = saleDate,
            State = saleDate.HasValue ? PnlLineState.Calculated : PnlLineState.NotCalculated,
            Quantity = quantity,
            ReturnedQuantity = returnedQuantity,
            RevenueAmount = revenue,
            NetRevenueAmount = netRevenue,
            ProductionCostAmount = cost,
            ControlDifferenceAmount = 0m,
            ProductionCostIsComplete = cost.HasValue ? costComplete : null,
            UnitTotalCost = unitCost,
            TotalCost = cost,
            CostQuantity = cost.HasValue ? quantity : null,
            CostIsComplete = cost.HasValue ? costComplete : null,
            CostStatus = OrderLineCostStatus.Resolved,
            CompletenessState = completeness,
        };
    }

    private static ITenantContext BuildTenantContext()
    {
        Mock<ITenantContext> tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(TenantId);
        return tenant.Object;
    }

    private static Mock<ITenantSettingsProvider> BuildSettingsProvider()
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
