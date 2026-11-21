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
using FluentAssertions;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Tests for the P&amp;L read handlers: the profile is resolved once per distinct date, a line
/// without a sale has no tax or margin, and the summary groups by product and period.
/// </summary>
public class PnlQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task GetLines_ManyLinesOfOneDate_LoadsTheTaxProfileHistoryOnce()
    {
        // Arrange
        PnlLinePage page = new PnlLinePage
        {
            Rows = new List<PnlLineRow>
            {
                Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m),
                Row(new DateOnly(2026, 8, 1), 200m, 180m, 20m),
            },
            ExcludedByCurrency = 3,
        };

        Mock<ITenantSettingsProvider> provider = BuildProvider();
        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(page),
            BuildTenantContext(),
            provider.Object);

        // Act
        PnlLinesResponse response = await handler.Handle(
            new GetPnlLinesQuery { Filter = new PnlQueryFilter(), Take = 100 },
            CancellationToken.None);

        // Assert
        response.Lines.Should().HaveCount(2);
        response.Currency.Should().Be("RUB");
        response.ExcludedByCurrency.Should().Be(3);

        // The history is the whole tax timeline, not one lookup per distinct sale date: the number
        // of round trips must not grow with the length of the report.
        provider.Verify(
            p => p.GetTaxProfileHistoryAsync(TenantId, It.IsAny<CancellationToken>()),
            Times.Once);

        provider.Verify(
            p => p.GetTaxProfileAtAsync(TenantId, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never);

        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
        response.Lines[0].TaxAdjustedMargin.Should().BeApproximately(74m, 0.0001m);
        response.Lines[0].Roi.Should().BeApproximately(8m, 0.0001m);
        response.Lines[0].ComponentsReconciled.Should().BeTrue();
    }

    [Fact]
    public async Task GetLines_WithoutTaxProfile_StillReportsThePreTaxMargin()
    {
        PnlLineRow row = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m);

        Mock<ITenantSettingsProvider> provider = BuildProviderWithoutTaxProfile();

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            provider.Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
        response.Lines[0].MarginPercent.Should().BeApproximately(0.8m, 0.0001m);
        response.Lines[0].Roi.Should().BeApproximately(8m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_IncompleteProductionCost_HasNoMarginEvenWithATaxProfile()
    {
        PnlLineRow row = Row(new DateOnly(2026, 8, 1), 100m, 90m, 0m, costComplete: false);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].Margin.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].MarginPercent.Should().BeNull();
        response.Lines[0].Roi.Should().BeNull();
        response.Lines[0].CostComplete.Should().BeFalse();
        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_LineWithoutSale_HasNoTaxOrMargin()
    {
        PnlLineRow row = Row(saleDate: null, revenue: null, netRevenue: null, cost: null);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeNull();
        response.Lines[0].Margin.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].MarginPercent.Should().BeNull();
        response.Lines[0].Roi.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_SetsHasProductLinkFromRowProductId()
    {
        PnlLineRow linked = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid());
        PnlLineRow unlinked = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { linked, unlinked } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].HasProductLink.Should().BeTrue();
        response.Lines[1].HasProductLink.Should().BeFalse();
    }

    [Fact]
    public async Task GetLines_ChannelProductWithoutResolvedProduct_StaysUnlinked()
    {
        Guid channelProductId = Guid.NewGuid();

        PnlLineRow row = Row(saleDate: new DateOnly(2026, 8, 1), revenue: 100m, netRevenue: 90m, cost: null);
        row.ChannelProductId = channelProductId;

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].ChannelProductId.Should().Be(channelProductId);
        response.Lines[0].ProductId.Should().BeNull();
        response.Lines[0].HasProductLink.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_GroupsByProductAndMonth()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 8, 15), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 9, 1), 200m, 180m, 20m, productId),
        };

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new FakeStore(new PnlLinePage(), rows),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Should().HaveCount(2);
        response.Rows[0].PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        response.Rows[0].Lines.Should().Be(2);
        response.Rows[0].RevenueAmount.Should().Be(200m);
        response.Rows[1].PeriodStart.Should().Be(new DateOnly(2026, 9, 1));
    }

    [Fact]
    public async Task GetSummary_MarksRowsWithoutProductLinkAsUnlinked()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 8, 1), 50m, 45m, 5m, productId: null),
        };

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new FakeStore(new PnlLinePage(), rows),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Day },
            CancellationToken.None);

        response.Rows.Should().HaveCount(2);
        response.Rows.Single(r => r.ProductId == productId).IsUnlinkedProduct.Should().BeFalse();
        response.Rows.Single(r => r.ProductId == null).IsUnlinkedProduct.Should().BeTrue();
    }

    [Fact]
    public async Task GetSummary_MixedCostCompleteness_ReportsTheMarginOfTheCoveredLinesOnly()
    {
        // One line with a complete cost, one with a partial cost and one without any cost. The
        // margin can speak only for the first line, so its percentages must use that line alone
        // while the coverage fields keep the rest of the revenue visible.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 250m, costComplete: false),
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, cost: null),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.RevenueAmount.Should().Be(3000m);
        row.ProductionCostAmount.Should().Be(550m);
        row.Margin.Should().Be(400m);

        row.Lines.Should().Be(3);
        row.LinesWithMargin.Should().Be(1);
        row.RevenueWithMarginAmount.Should().Be(1000m);
        row.ProductionCostWithMarginAmount.Should().Be(300m);
        row.RevenueWithoutCostAmount.Should().Be(2000m);
        row.IsMarginComplete.Should().BeFalse();

        row.RevenueWithMarginAmount.Should().Be(row.RevenueAmount!.Value - row.RevenueWithoutCostAmount);

        row.MarginPercent.Should().BeApproximately(0.4m, 0.0001m);
        row.Roi.Should().BeApproximately(4m / 3m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_TaxesStayAttributedToTheLinesThatCarryThem()
    {
        // The tax layer is an intersection of two populations: a known margin and a tax profile.
        // The pre-tax margin must not absorb a tax computed on revenue it does not cover.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 250m, costComplete: false),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.EstimatedTax.Should().Be(120m);
        row.Margin.Should().Be(400m);
        row.TaxAdjustedMargin.Should().Be(340m);
    }

    [Fact]
    public async Task GetSummary_AllCostsComplete_IsMarginCompleteAndReconciles()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, productId),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.IsMarginComplete.Should().BeTrue();
        row.LinesWithMargin.Should().Be(2);
        row.RevenueWithMarginAmount.Should().Be(200m);
        row.ProductionCostWithMarginAmount.Should().Be(20m);
        row.RevenueWithoutCostAmount.Should().Be(0m);
        row.Margin.Should().Be(160m);
        row.MarginPercent.Should().BeApproximately(0.8m, 0.0001m);
        row.Roi.Should().BeApproximately(8m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_NoCostAtAll_ReportsNoDerivedMarginButKeepsTheRevenue()
    {
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, cost: null),
            Row(new DateOnly(2026, 8, 2), 1000m, 700m, cost: null),
            Row(new DateOnly(2026, 8, 3), 1000m, 700m, cost: null),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Margin.Should().BeNull();
        row.MarginPercent.Should().BeNull();
        row.Roi.Should().BeNull();
        row.LinesWithMargin.Should().Be(0);
        row.RevenueWithMarginAmount.Should().Be(0m);
        row.ProductionCostWithMarginAmount.Should().Be(0m);
        row.RevenueWithoutCostAmount.Should().Be(3000m);
        row.IsMarginComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_CompletenessState_ReportsTheMostSevereLine()
    {
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.ClosedWithException),
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Overdue),
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Provisional),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Single().CompletenessState.Should().Be(PnlLineCompleteness.Overdue);
    }

    [Fact]
    public async Task GetSummary_SplitsRevenueByCompletenessState()
    {
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Complete),
            Row(new DateOnly(2026, 8, 1), 200m, 180m, 20m, completeness: PnlLineCompleteness.Provisional),
            Row(new DateOnly(2026, 8, 1), 300m, 270m, 30m, completeness: PnlLineCompleteness.Overdue),
            Row(new DateOnly(2026, 8, 1), 400m, 360m, 40m, completeness: PnlLineCompleteness.ClosedWithException),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Filter = new PnlQueryFilter { Completeness = "all" },
                Granularity = PnlPeriodGranularity.Month,
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.RevenueAmount.Should().Be(1000m);
        row.ProvisionalRevenueAmount.Should().Be(500m);
        row.OverdueRevenueAmount.Should().Be(300m);
        row.ClosedWithExceptionRevenueAmount.Should().Be(400m);
        row.ProvisionalRevenueAmount.Should().BeGreaterThanOrEqualTo(row.OverdueRevenueAmount);
        row.CompletenessState.Should().Be(PnlLineCompleteness.Overdue);
    }

    [Fact]
    public async Task GetSummary_DefaultCompleteness_SelectsTheFinalStates()
    {
        FakeStore store = new FakeStore(new PnlLinePage(), new List<PnlLineRow>());

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            BuildTenantContext(),
            BuildProvider().Object);

        await handler.Handle(new GetPnlSummaryQuery(), CancellationToken.None);

        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.CompletenessStates.Should().BeEquivalentTo(
            new[] { PnlLineCompleteness.Complete, PnlLineCompleteness.ClosedWithException });
    }

    [Fact]
    public async Task GetSummary_AllCompleteness_LeavesTheStoreUnrestricted()
    {
        FakeStore store = new FakeStore(new PnlLinePage(), new List<PnlLineRow>());

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            BuildTenantContext(),
            BuildProvider().Object);

        await handler.Handle(
            new GetPnlSummaryQuery { Filter = new PnlQueryFilter { Completeness = "all" } },
            CancellationToken.None);

        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.CompletenessStates.Should().BeNull();
    }

    [Fact]
    public async Task GetLines_DefaultCompleteness_SelectsTheFinalStates()
    {
        FakeStore store = new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow>() });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            store,
            BuildTenantContext(),
            BuildProvider().Object);

        await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.CompletenessStates.Should().BeEquivalentTo(
            new[] { PnlLineCompleteness.Complete, PnlLineCompleteness.ClosedWithException });
    }

    [Fact]
    public async Task GetLines_LineWithoutSale_ReportsItsNetRevenueAsTheResult()
    {
        // A non-buyout carries only the delivery charge: no unit left the seller, so no cost of goods
        // applies and the negative net revenue is the known result of the line.
        PnlLineRow row = Row(
            saleDate: null,
            revenue: null,
            netRevenue: -53.84m,
            cost: null,
            periodDate: new DateOnly(2026, 8, 5));

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].Margin.Should().Be(-53.84m);
        response.Lines[0].PeriodDate.Should().Be(new DateOnly(2026, 8, 5));
        response.Lines[0].MarginPercent.Should().BeNull();
        response.Lines[0].Roi.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_LineWithoutSale_AddsItsResultToThePeriodOfItsCharge()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m, productId),
            Row(
                saleDate: null,
                revenue: null,
                netRevenue: -53.84m,
                cost: null,
                productId: productId,
                periodDate: new DateOnly(2026, 8, 5)),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Lines.Should().Be(2);
        row.RevenueAmount.Should().Be(1000m);
        row.NetRevenueAmount.Should().Be(646.16m);
        row.Margin.Should().Be(346.16m);
        row.LinesWithMargin.Should().Be(2);
        row.IsMarginComplete.Should().BeTrue();
        row.RevenueWithMarginAmount.Should().Be(1000m);
        row.RevenueWithoutCostAmount.Should().Be(0m);
        row.MarginPercent.Should().BeApproximately(0.34616m, 0.00001m);
    }

    [Fact]
    public async Task GetSummary_LineWithoutAnyDate_IsNotGrouped()
    {
        List<PnlLineRow> rows = new()
        {
            Row(saleDate: null, revenue: null, netRevenue: -53.84m, cost: null),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLines_ClosedWithExceptionLine_IsNotReportedAsProvisional()
    {
        PnlLineRow row = Row(
            new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.ClosedWithException);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(
            new GetPnlLinesQuery { Filter = new PnlQueryFilter { Completeness = "all" } },
            CancellationToken.None);

        response.Lines[0].CompletenessState.Should().Be(PnlLineCompleteness.ClosedWithException);
        response.Lines[0].IsProvisional.Should().BeFalse();
    }

    [Fact]
    public async Task GetLines_TwoDatesAcrossAProfileChange_ApplyTheProfileOfEachDate()
    {
        // A single history load must still resolve the right profile per date: the report covers
        // both sides of the change, so a stale flat rate would misstate one of them.
        PnlLineRow beforeChange = Row(new DateOnly(2025, 12, 31), revenue: 100m, netRevenue: 90m, cost: 10m);
        PnlLineRow afterChange = Row(new DateOnly(2026, 1, 1), revenue: 100m, netRevenue: 90m, cost: 10m);

        Mock<ITenantSettingsProvider> provider = new Mock<ITenantSettingsProvider>();

        provider
            .Setup(p => p.GetAccountingSettingsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "UTC" });

        provider
            .Setup(p => p.GetTaxProfileHistoryAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TenantTaxProfileSnapshot>
            {
                new()
                {
                    Regime = TaxRegime.UsnIncome,
                    RatePercent = 15m,
                    Vat = VatType.None,
                    ValidFrom = new DateOnly(2026, 1, 1),
                },
                new()
                {
                    Regime = TaxRegime.UsnIncome,
                    RatePercent = 6m,
                    Vat = VatType.None,
                    ValidFrom = new DateOnly(2025, 1, 1),
                },
            });

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { beforeChange, afterChange } }),
            BuildTenantContext(),
            provider.Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeApproximately(6m, 0.0001m);
        response.Lines[1].EstimatedTax.Should().BeApproximately(15m, 0.0001m);
    }

    [Fact]
    public async Task GetLines_DateBeforeEveryProfile_ReportsNoTax()
    {
        PnlLineRow row = Row(new DateOnly(2024, 12, 31), revenue: 100m, netRevenue: 90m, cost: 10m);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { row } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].EstimatedTax.Should().BeNull();
        response.Lines[0].TaxAdjustedMargin.Should().BeNull();
        response.Lines[0].Margin.Should().BeApproximately(80m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_ControlDifference_SumsOnlyTheLinesOfItsGroup()
    {
        // The figure is summed per group. Rescanning the whole page for every group was the
        // quadratic part of the summary; the sums themselves must stay exactly per group.
        Guid firstProduct = Guid.NewGuid();
        Guid secondProduct = Guid.NewGuid();

        PnlLineRow first = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, firstProduct);
        first.ControlDifferenceAmount = 1.5m;

        PnlLineRow second = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, firstProduct);
        second.ControlDifferenceAmount = -0.5m;

        PnlLineRow third = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, secondProduct);
        third.ControlDifferenceAmount = 7m;

        PnlSummaryResponse response = await BuildSummaryHandler(new List<PnlLineRow> { first, second, third })
            .Handle(new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month }, CancellationToken.None);

        response.Rows.Should().HaveCount(2);
        response.Rows.Single(r => r.ProductId == firstProduct).ControlDifferenceAmount.Should().Be(1m);
        response.Rows.Single(r => r.ProductId == secondProduct).ControlDifferenceAmount.Should().Be(7m);
    }

    [Fact]
    public async Task GetSummary_WithoutControlDifference_KeepsTheFigureUnknown()
    {
        PnlLineRow row = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m);
        row.ControlDifferenceAmount = null;

        PnlSummaryResponse response = await BuildSummaryHandler(new List<PnlLineRow> { row })
            .Handle(new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month }, CancellationToken.None);

        response.Rows.Single().ControlDifferenceAmount.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_ReadsTheLinesOnceAndWithoutMaterialisingThem()
    {
        // The summary must fold rows as they arrive: one pass over the stream, every row pulled.
        // A second enumeration would mean the store was asked for the page twice.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m),
            Row(new DateOnly(2026, 8, 2), 200m, 180m, 20m),
        };

        FakeStore store = new FakeStore(new PnlLinePage(), rows);

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            BuildTenantContext(),
            BuildProvider().Object);

        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Should().ContainSingle();
        store.StreamCount.Should().Be(1);
        store.PulledRows.Should().Be(2);
    }

    [Fact]
    public async Task GetLines_ReportsTheOrderedAndTheSoldQuantityApart()
    {
        // A non-buyout was ordered but never sold: it must add to the ordered units only, otherwise
        // a per-unit margin would be divided by units that never left the seller.
        PnlLineRow sold = Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, quantity: 3);
        PnlLineRow notBoughtOut = Row(
            saleDate: null,
            revenue: null,
            netRevenue: -53.84m,
            cost: null,
            periodDate: new DateOnly(2026, 8, 5),
            quantity: 2);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { sold, notBoughtOut } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].QuantityOrdered.Should().Be(3);
        response.Lines[0].QuantitySold.Should().Be(3);
        response.Lines[1].QuantityOrdered.Should().Be(2);
        response.Lines[1].QuantitySold.Should().Be(0);
    }

    [Fact]
    public async Task GetLines_ReturnedUnit_IsOrderedButNoLongerSold()
    {
        // The unit sold and came back: the money of the return is what is left, and no cost of goods
        // was consumed, so the line must not be divided by a cost it does not carry.
        PnlLineRow returned = Row(
            new DateOnly(2026, 8, 1),
            revenue: 0m,
            netRevenue: 0m,
            cost: null,
            quantity: 1,
            returnedQuantity: 1);

        GetPnlLinesQueryHandler handler = new GetPnlLinesQueryHandler(
            new FakeStore(new PnlLinePage { Rows = new List<PnlLineRow> { returned } }),
            BuildTenantContext(),
            BuildProvider().Object);

        PnlLinesResponse response = await handler.Handle(new GetPnlLinesQuery(), CancellationToken.None);

        response.Lines[0].QuantityOrdered.Should().Be(1);
        response.Lines[0].QuantitySold.Should().Be(0);
        response.Lines[0].Margin.Should().Be(0m);
    }

    [Fact]
    public async Task GetSummary_SumsTheOrderedAndTheSoldQuantitySeparately()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId, quantity: 3),
            Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, productId, quantity: 1),
            Row(
                saleDate: null,
                revenue: null,
                netRevenue: -53.84m,
                cost: null,
                productId: productId,
                periodDate: new DateOnly(2026, 8, 5),
                quantity: 2),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.QuantityOrdered.Should().Be(6);
        row.QuantitySold.Should().Be(4);
        row.Lines.Should().Be(3);
    }

    [Fact]
    public async Task GetSummary_TotalGranularity_FoldsTheWholeRangeIntoOneRow()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId, quantity: 2),
            Row(new DateOnly(2026, 9, 15), 200m, 180m, 20m, productId, quantity: 3),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Total,
                Filter = new PnlQueryFilter { From = new DateOnly(2026, 8, 1) },
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        row.Lines.Should().Be(2);
        row.RevenueAmount.Should().Be(300m);
        row.QuantityOrdered.Should().Be(5);
    }

    [Fact]
    public async Task GetSummary_TotalWithoutARange_StartsAtTheEarliestPeriodOfTheGroup()
    {
        // Without a range the grouping key has no real date behind it. Reporting the sentinel would
        // leak a 0001-01-01 into the payload and be read as a real period start.
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 9, 15), 100m, 90m, 10m, productId),
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Total },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        row.PeriodStart.Should().NotBe(DateOnly.MinValue);
    }

    [Fact]
    public async Task GetSummary_TotalGranularity_KeepsTheProductGroupsApart()
    {
        // The whole range is one period, not one group: two products stay two rows per period.
        Guid firstProduct = Guid.NewGuid();
        Guid secondProduct = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, firstProduct),
            Row(new DateOnly(2026, 9, 1), 100m, 90m, 10m, firstProduct),
            Row(new DateOnly(2026, 9, 2), 100m, 90m, 10m, secondProduct),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Total,
                Filter = new PnlQueryFilter { From = new DateOnly(2026, 8, 1) },
            },
            CancellationToken.None);

        response.Rows.Should().HaveCount(2);
        response.Rows.Should().OnlyContain(r => r.PeriodStart == new DateOnly(2026, 8, 1));
        response.Rows.Single(r => r.ProductId == firstProduct).Lines.Should().Be(2);
        response.Rows.Single(r => r.ProductId == secondProduct).Lines.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_GroupedByChannel_ReportsOneRowPerChannel()
    {
        Guid firstChannel = Guid.NewGuid();
        Guid secondChannel = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid(), quantity: 2, channelId: firstChannel),
            Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, Guid.NewGuid(), quantity: 3, channelId: firstChannel),
            Row(new DateOnly(2026, 8, 3), 500m, 450m, 50m, Guid.NewGuid(), quantity: 1, channelId: secondChannel),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Month,
                GroupBy = PnlSummaryGroupBy.Channel,
            },
            CancellationToken.None);

        response.Rows.Should().HaveCount(2);

        PnlSummaryRowView first = response.Rows.Single(r => r.ChannelId == firstChannel);
        first.Lines.Should().Be(2);
        first.RevenueAmount.Should().Be(200m);
        first.Margin.Should().Be(160m);
        first.QuantityOrdered.Should().Be(5);
        first.ProductId.Should().BeNull();
        first.IsUnlinkedProduct.Should().BeFalse();

        PnlSummaryRowView second = response.Rows.Single(r => r.ChannelId == secondChannel);
        second.Lines.Should().Be(1);
        second.RevenueAmount.Should().Be(500m);
        second.ProductId.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_GroupedByChannel_KeepsLinesWithoutAProductLink()
    {
        // A channel group is about the money of the channel, not about the catalogue: a line whose
        // product is not resolved yet still belongs to it and must not disappear.
        Guid channelId = Guid.NewGuid();
        Guid linkedProduct = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, linkedProduct, channelId: channelId),
            Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, productId: null, channelId: channelId),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Month,
                GroupBy = PnlSummaryGroupBy.Channel,
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.ChannelId.Should().Be(channelId);
        row.Lines.Should().Be(2);
        row.IsUnlinkedProduct.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_GroupedByProduct_LeavesTheChannelOfTheGroupEmpty()
    {
        // The dimension decides the identifier: a product group must not expose one of its channels
        // as if the group belonged to it.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Month,
                GroupBy = PnlSummaryGroupBy.Product,
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.ChannelId.Should().BeNull();
        row.ProductId.Should().NotBeNull();
    }

    [Fact]
    public async Task GetSummary_GroupedByNone_FoldsEveryProductIntoOneRowPerPeriod()
    {
        // The whole business is the dimension here: the per-product percentages cannot be averaged,
        // so the trend must come from one aggregate row per period and not from client arithmetic.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
            Row(new DateOnly(2026, 8, 2), 200m, 180m, 20m, Guid.NewGuid()),
            Row(new DateOnly(2026, 9, 1), 50m, 45m, cost: null, productId: Guid.NewGuid()),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Month,
                GroupBy = PnlSummaryGroupBy.None,
            },
            CancellationToken.None);

        response.GroupBy.Should().Be(PnlSummaryGroupBy.None);
        response.Rows.Should().HaveCount(2);

        PnlSummaryRowView august = response.Rows.Single(r => r.PeriodStart == new DateOnly(2026, 8, 1));
        august.Lines.Should().Be(2);
        august.RevenueAmount.Should().Be(300m);
        august.Margin.Should().Be(240m);
        august.MarginPercent.Should().Be(0.8m);
        august.IsMarginComplete.Should().BeTrue();

        PnlSummaryRowView september = response.Rows.Single(r => r.PeriodStart == new DateOnly(2026, 9, 1));
        september.Margin.Should().BeNull();
        september.RevenueWithoutCostAmount.Should().Be(50m);
        september.IsMarginComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_GroupedByNone_CarriesNeitherIdentifierAndIsNoUnlinkedProduct()
    {
        // An empty product identifier in a dimensionless row would read as an unresolved product and
        // make the UI render a catalogue bucket that does not exist.
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Month,
                GroupBy = PnlSummaryGroupBy.None,
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.ProductId.Should().BeNull();
        row.ChannelId.Should().BeNull();
        row.IsUnlinkedProduct.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_GroupedByNoneWithTotalGranularity_ReportsOneRowForTheWholeRange()
    {
        List<PnlLineRow> rows = new()
        {
            Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
            Row(new DateOnly(2026, 9, 15), 200m, 180m, 20m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await BuildSummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Total,
                GroupBy = PnlSummaryGroupBy.None,
                Filter = new PnlQueryFilter { From = new DateOnly(2026, 8, 1) },
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        row.Lines.Should().Be(2);
        row.RevenueAmount.Should().Be(300m);
    }

    [Fact]
    public async Task GetSummary_EchoesTheGroupingDimension()
    {
        PnlSummaryResponse productGrouping = await BuildSummaryHandler(new List<PnlLineRow>())
            .Handle(new GetPnlSummaryQuery { GroupBy = PnlSummaryGroupBy.Product }, CancellationToken.None);

        PnlSummaryResponse channelGrouping = await BuildSummaryHandler(new List<PnlLineRow>())
            .Handle(new GetPnlSummaryQuery { GroupBy = PnlSummaryGroupBy.Channel }, CancellationToken.None);

        PnlSummaryResponse dimensionlessGrouping = await BuildSummaryHandler(new List<PnlLineRow>())
            .Handle(new GetPnlSummaryQuery { GroupBy = PnlSummaryGroupBy.None }, CancellationToken.None);

        productGrouping.GroupBy.Should().Be(PnlSummaryGroupBy.Product);
        channelGrouping.GroupBy.Should().Be(PnlSummaryGroupBy.Channel);
        dimensionlessGrouping.GroupBy.Should().Be(PnlSummaryGroupBy.None);
    }

    private static GetPnlSummaryQueryHandler BuildSummaryHandler(IReadOnlyList<PnlLineRow> rows)
    {
        return new GetPnlSummaryQueryHandler(
            new FakeStore(new PnlLinePage(), rows),
            BuildTenantContext(),
            BuildProvider().Object);
    }

    private static PnlLineRow Row(
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
        int returnedQuantity = 0)
    {
        return new PnlLineRow
        {
            OrderLineId = Guid.NewGuid(),
            ChannelId = channelId ?? Guid.NewGuid(),
            ProductId = productId,
            Currency = "RUB",
            SaleDate = saleDate,
            PeriodDate = periodDate ?? saleDate,
            State = saleDate.HasValue ? PnlLineState.Calculated : PnlLineState.NotCalculated,
            Quantity = quantity,
            ReturnedQuantity = returnedQuantity,
            RevenueAmount = revenue,
            NetRevenueAmount = netRevenue,
            ProductionCostAmount = cost,
            ControlDifferenceAmount = 0m,
            ProductionCostIsComplete = cost.HasValue ? costComplete : null,
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

    private static Mock<ITenantSettingsProvider> BuildProvider()
    {
        Mock<ITenantSettingsProvider> provider = new Mock<ITenantSettingsProvider>();

        provider
            .Setup(p => p.GetAccountingSettingsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountingSettings { Currency = "RUB", TimeZoneId = "UTC" });

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

    private static Mock<ITenantSettingsProvider> BuildProviderWithoutTaxProfile()
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

    private sealed class FakeStore : IPnlReadStore
    {
        private readonly PnlLinePage _page;
        private readonly IReadOnlyList<PnlLineRow> _rows;

        public FakeStore(PnlLinePage page, IReadOnlyList<PnlLineRow>? rows = null)
        {
            _page = page;
            _rows = rows ?? page.Rows;
        }

        public PnlLineFilter? LastFilter { get; private set; }

        /// <summary>Gets the number of times the summary stream was opened.</summary>
        public int StreamCount { get; private set; }

        /// <summary>Gets the number of rows the handler pulled from the stream.</summary>
        public int PulledRows { get; private set; }

        public Task<PnlLinePage> GetLinesAsync(
            string currency,
            PnlLineFilter filter,
            int take,
            string? afterToken,
            CancellationToken cancellationToken)
        {
            LastFilter = filter;
            return Task.FromResult(_page);
        }

        public async IAsyncEnumerable<PnlLineRow> StreamForSummaryAsync(
            string currency,
            PnlLineFilter filter,
            CancellationToken cancellationToken)
        {
            LastFilter = filter;
            StreamCount++;

            foreach (PnlLineRow row in _rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                PulledRows++;
                yield return row;
            }

            await Task.CompletedTask;
        }

        public Task<PnlDiagnosticsResult> GetDiagnosticsAsync(
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new PnlDiagnosticsResult());
        }
    }
}


