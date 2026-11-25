using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Delobytes.App.Backend.Catalog.Contracts.Lookup;
using Delobytes.App.Backend.Contracts.Accounting;
using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Finance.Application.Interfaces;
using Delobytes.App.Backend.Finance.Application.ProductTable;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.Ranking;
using Delobytes.App.Backend.Finance.Application.ReadModels;
using Delobytes.App.Backend.Finance.Domain.Enums;
using Delobytes.App.Backend.Tests.Application.Finance.Fakes;
using FluentAssertions;
using FluentValidation;
using Moq;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// P&amp;L summary: totals, grouping, period granularity and completeness filters over one stream of lines.
/// </summary>
public class GetPnlSummaryQueryHandlerTests
{
    [Fact]
    public async Task GetSummary_GroupsByProductAndMonth()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            PnlTestData.Row(new DateOnly(2026, 8, 15), 100m, 90m, 10m, productId),
            PnlTestData.Row(new DateOnly(2026, 9, 1), 200m, 180m, 20m, productId),
        };

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 50m, 45m, 5m, productId: null),
        };

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 250m, costComplete: false),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, cost: null),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
        row.NetRevenueWithMarginAmount.Should().Be(700m);
        row.NetRevenueWithoutCostAmount.Should().Be(1400m);
        row.RevenueWithoutCostAmount.Should().Be(2000m);
        row.IsMarginComplete.Should().BeFalse();

        row.RevenueWithMarginAmount.Should().Be(row.RevenueAmount!.Value - row.RevenueWithoutCostAmount);
        row.NetRevenueWithMarginAmount.Should().Be(
            row.NetRevenueAmount!.Value - row.NetRevenueWithoutCostAmount);

        // The contribution margin is a share of the net revenue the margin covers: 400 / 700.
        row.ContributionMarginPercent.Should().BeApproximately(400m / 700m * 100m, 0.0001m);
        row.ReturnOnSoldCostPercent.Should().BeApproximately(400m / 3m, 0.0001m);
        row.InvestedCostAmount.Should().Be(300m);
        row.LinesWithInvestedCost.Should().Be(1);
        row.IsRoiComplete.Should().BeFalse();
        row.RoiPercent.Should().BeApproximately(400m / 3m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_TaxesStayAttributedToTheLinesThatCarryThem()
    {
        // The tax layer is an intersection of two populations: a known margin and a tax profile.
        // The pre-tax margin must not absorb a tax computed on revenue it does not cover.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 250m, costComplete: false),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.EstimatedTax.Should().Be(120m);
        row.Margin.Should().Be(400m);
        row.TaxAdjustedMargin.Should().Be(340m);
    }

    [Fact]
    public async Task GetSummary_TaxAdjustedPercentages_StayUnknownUntilTheWholeGroupIsTaxed()
    {
        // The range reaches back before the first profile, so one of the two lines carries no tax.
        // The tax layer covers half of the group: its share must stay unknown rather than be divided
        // by a base that also holds the untaxed line.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2025, 12, 31), 1000m, 700m, 300m),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Total },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Margin.Should().Be(800m);
        row.TaxAdjustedMargin.Should().Be(340m);
        row.LinesWithTaxAdjustedMargin.Should().Be(1);
        row.RevenueWithTaxAdjustedMarginAmount.Should().Be(1000m);
        row.NetRevenueWithTaxAdjustedMarginAmount.Should().Be(700m);
        row.IsTaxAdjustedMarginComplete.Should().BeFalse();
        row.IsTaxAdjustedRoiComplete.Should().BeFalse();
        row.TaxAdjustedContributionMarginPercent.Should().BeNull();
        row.TaxAdjustedRoiPercent.Should().BeNull();

        // The covered tax base stays inside the margin-covered revenue: the coverage parts are disjoint.
        row.RevenueWithTaxAdjustedMarginAmount.Should().BeLessThan(row.RevenueWithMarginAmount);
    }

    [Fact]
    public async Task GetSummary_TaxAdjustedPercentages_UseTheCoveredPopulationAlone()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 500m, 350m, 150m),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Total },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Margin.Should().Be(600m);
        row.TaxAdjustedMargin.Should().Be(510m);
        row.IsTaxAdjustedMarginComplete.Should().BeTrue();
        row.IsTaxAdjustedRoiComplete.Should().BeTrue();
        row.LinesWithTaxAdjustedMargin.Should().Be(2);
        row.RevenueWithTaxAdjustedMarginAmount.Should().Be(1500m);
        row.NetRevenueWithTaxAdjustedMarginAmount.Should().Be(1050m);
        row.InvestedCostWithTaxAdjustedMarginAmount.Should().Be(450m);
        row.TaxAdjustedContributionMarginPercent.Should().BeApproximately(510m / 1050m * 100m, 0.0001m);
        row.TaxAdjustedRoiPercent.Should().BeApproximately(510m / 450m * 100m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_TaxAdjustedRoi_StaysUnknownWhenSomeLinesHaveNoInvestment()
    {
        // A non-buyout carries no cost fact: it has a taxed margin but no investment to divide by.
        // The contribution share still describes the whole group; the return does not.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            PnlTestData.Row(
                saleDate: null,
                revenue: null,
                netRevenue: -100m,
                cost: null,
                periodDate: new DateOnly(2026, 8, 5)),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Total },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.TaxAdjustedMargin.Should().Be(240m);
        row.LinesWithTaxAdjustedMargin.Should().Be(2);
        row.IsTaxAdjustedMarginComplete.Should().BeTrue();
        row.TaxAdjustedContributionMarginPercent.Should().BeApproximately(240m / 600m * 100m, 0.0001m);

        row.LinesWithTaxAdjustedInvestedCost.Should().Be(1);
        row.InvestedCostWithTaxAdjustedMarginAmount.Should().Be(300m);
        row.IsTaxAdjustedRoiComplete.Should().BeFalse();
        row.TaxAdjustedRoiPercent.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_AllCostsComplete_IsMarginCompleteAndReconciles()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, productId),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.IsMarginComplete.Should().BeTrue();
        row.LinesWithMargin.Should().Be(2);
        row.RevenueWithMarginAmount.Should().Be(200m);
        row.ProductionCostWithMarginAmount.Should().Be(20m);
        row.NetRevenueWithMarginAmount.Should().Be(180m);
        row.NetRevenueWithoutCostAmount.Should().Be(0m);
        row.RevenueWithoutCostAmount.Should().Be(0m);
        row.Margin.Should().Be(160m);
        row.MarginFromSalesAmount.Should().Be(160m);
        row.MarginWithoutSaleAmount.Should().BeNull();
        row.ContributionMarginPercent.Should().BeApproximately(160m / 180m * 100m, 0.0001m);
        row.ReturnOnSoldCostPercent.Should().BeApproximately(800m, 0.0001m);
        row.InvestedCostAmount.Should().Be(20m);
        row.LinesWithInvestedCost.Should().Be(2);
        row.IsRoiComplete.Should().BeTrue();
        row.RoiPercent.Should().BeApproximately(800m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_SplitsTheMarginBetweenSoldAndUnsoldLines()
    {
        // A non-buyout never sold, so no cost of goods applies and its result is its net revenue:
        // the delivery charge of an unsold unit lands in the margin of the group. Reported apart
        // from the sales margin, it tells a negative total apart from a loss on the sales.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m),
            PnlTestData.Row(
                saleDate: null,
                revenue: null,
                netRevenue: -50m,
                cost: null,
                periodDate: new DateOnly(2026, 8, 2),
                deliveryFee: -50m),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Margin.Should().Be(350m);
        row.MarginFromSalesAmount.Should().Be(400m);
        row.MarginWithoutSaleAmount.Should().Be(-50m);
        row.Margin.Should().Be(row.MarginFromSalesAmount + row.MarginWithoutSaleAmount);
    }

    [Fact]
    public async Task GetSummary_NoCostAtAll_ReportsNoDerivedMarginButKeepsTheRevenue()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, cost: null),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 1000m, 700m, cost: null),
            PnlTestData.Row(new DateOnly(2026, 8, 3), 1000m, 700m, cost: null),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Margin.Should().BeNull();
        row.ContributionMarginPercent.Should().BeNull();
        row.RoiPercent.Should().BeNull();
        row.LinesWithMargin.Should().Be(0);
        row.RevenueWithMarginAmount.Should().Be(0m);
        row.ProductionCostWithMarginAmount.Should().Be(0m);
        row.NetRevenueWithMarginAmount.Should().Be(0m);
        row.NetRevenueWithoutCostAmount.Should().Be(2100m);
        row.RevenueWithoutCostAmount.Should().Be(3000m);
        row.InvestedCostAmount.Should().Be(0m);
        row.LinesWithInvestedCost.Should().Be(0);
        row.IsRoiComplete.Should().BeFalse();
        row.IsMarginComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_CompletenessState_ReportsTheMostSevereLine()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.ClosedWithException),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Overdue),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Provisional),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Single().CompletenessState.Should().Be(PnlLineCompleteness.Overdue);
    }

    [Fact]
    public async Task GetSummary_SplitsRevenueByCompletenessState()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, completeness: PnlLineCompleteness.Complete),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 200m, 180m, 20m, completeness: PnlLineCompleteness.Provisional),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 300m, 270m, 30m, completeness: PnlLineCompleteness.Overdue),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 400m, 360m, 40m, completeness: PnlLineCompleteness.ClosedWithException),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage(), new List<PnlLineRow>());

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        await handler.Handle(new GetPnlSummaryQuery(), CancellationToken.None);

        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.CompletenessStates.Should().BeEquivalentTo(
            new[] { PnlLineCompleteness.Complete, PnlLineCompleteness.ClosedWithException });
    }

    [Fact]
    public async Task GetSummary_AllCompleteness_LeavesTheStoreUnrestricted()
    {
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage(), new List<PnlLineRow>());

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        await handler.Handle(
            new GetPnlSummaryQuery { Filter = new PnlQueryFilter { Completeness = "all" } },
            CancellationToken.None);

        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.CompletenessStates.Should().BeNull();
    }

    [Fact]
    public async Task GetSummary_NonBuyoutKeepsItsInvestmentInTheRoiBase()
    {
        // The returned unit earns nothing and its cost is not charged, but the money is still tied
        // up: dropping it from the denominator would overstate the return on the whole group.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m, unitCost: 300m),
            PnlTestData.Row(
                saleDate: null,
                revenue: null,
                netRevenue: -100m,
                cost: null,
                periodDate: new DateOnly(2026, 8, 5),
                unitCost: 300m),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.Margin.Should().Be(300m);
        row.NetRevenueWithMarginAmount.Should().Be(600m);
        row.InvestedCostAmount.Should().Be(600m);
        row.LinesWithInvestedCost.Should().Be(2);
        row.IsRoiComplete.Should().BeTrue();
        row.RoiPercent.Should().BeApproximately(50m, 0.0001m);
        row.ContributionMarginPercent.Should().BeApproximately(50m, 0.0001m);

        // The non-buyout pays no tax on a sale it never made, so it does not block the tax layer:
        // 340 - 100 on a net revenue of 600 and an investment of 600.
        row.TaxAdjustedMargin.Should().Be(240m);
        row.LinesWithTaxAdjustedMargin.Should().Be(2);
        row.IsTaxAdjustedMarginComplete.Should().BeTrue();
        row.TaxAdjustedContributionMarginPercent.Should().BeApproximately(40m, 0.0001m);
        row.IsTaxAdjustedRoiComplete.Should().BeTrue();
        row.TaxAdjustedRoiPercent.Should().BeApproximately(40m, 0.0001m);
    }

    [Fact]
    public async Task GetSummary_LineWithoutASale_AddsItsResultToThePeriodOfItsCharge()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 1000m, 700m, 300m, productId),
            PnlTestData.Row(
                saleDate: null,
                revenue: null,
                netRevenue: -53.84m,
                cost: null,
                productId: productId,
                periodDate: new DateOnly(2026, 8, 5)),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
        row.NetRevenueWithMarginAmount.Should().Be(646.16m);
        row.ContributionMarginPercent.Should().BeApproximately(346.16m / 646.16m * 100m, 0.00001m);
    }

    [Fact]
    public async Task GetSummary_LineWithoutAnyDate_IsNotGrouped()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(saleDate: null, revenue: null, netRevenue: -53.84m, cost: null),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummary_ControlDifference_SumsOnlyTheLinesOfItsGroup()
    {
        // The figure is summed per group. Rescanning the whole page for every group was the
        // quadratic part of the summary; the sums themselves must stay exactly per group.
        Guid firstProduct = Guid.NewGuid();
        Guid secondProduct = Guid.NewGuid();

        PnlLineRow first = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, firstProduct);
        first.ControlDifferenceAmount = 1.5m;

        PnlLineRow second = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, firstProduct);
        second.ControlDifferenceAmount = -0.5m;

        PnlLineRow third = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, secondProduct);
        third.ControlDifferenceAmount = 7m;

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(new List<PnlLineRow> { first, second, third })
            .Handle(new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month }, CancellationToken.None);

        response.Rows.Should().HaveCount(2);
        response.Rows.Single(r => r.ProductId == firstProduct).ControlDifferenceAmount.Should().Be(1m);
        response.Rows.Single(r => r.ProductId == secondProduct).ControlDifferenceAmount.Should().Be(7m);
    }

    [Fact]
    public async Task GetSummary_WithoutControlDifference_KeepsTheFigureUnknown()
    {
        PnlLineRow row = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m);
        row.ControlDifferenceAmount = null;

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(new List<PnlLineRow> { row })
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 200m, 180m, 20m),
        };

        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage(), rows);

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        response.Rows.Should().ContainSingle();
        store.StreamCount.Should().Be(1);
        store.PulledRows.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_SumsTheOrderedAndTheSoldQuantitySeparately()
    {
        Guid productId = Guid.NewGuid();

        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId, quantity: 3),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, productId, quantity: 1),
            PnlTestData.Row(
                saleDate: null,
                revenue: null,
                netRevenue: -53.84m,
                cost: null,
                productId: productId,
                periodDate: new DateOnly(2026, 8, 5),
                quantity: 2),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId, quantity: 2),
            PnlTestData.Row(new DateOnly(2026, 9, 15), 200m, 180m, 20m, productId, quantity: 3),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 9, 15), 100m, 90m, 10m, productId),
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, firstProduct),
            PnlTestData.Row(new DateOnly(2026, 9, 1), 100m, 90m, 10m, firstProduct),
            PnlTestData.Row(new DateOnly(2026, 9, 2), 100m, 90m, 10m, secondProduct),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid(), quantity: 2, channelId: firstChannel),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, Guid.NewGuid(), quantity: 3, channelId: firstChannel),
            PnlTestData.Row(new DateOnly(2026, 8, 3), 500m, 450m, 50m, Guid.NewGuid(), quantity: 1, channelId: secondChannel),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, linkedProduct, channelId: channelId),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 100m, 90m, 10m, productId: null, channelId: channelId),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
            PnlTestData.Row(new DateOnly(2026, 8, 2), 200m, 180m, 20m, Guid.NewGuid()),
            PnlTestData.Row(new DateOnly(2026, 9, 1), 50m, 45m, cost: null, productId: Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
        august.ContributionMarginPercent.Should().BeApproximately(240m / 270m * 100m, 0.0001m);
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId: null),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
            PnlTestData.Row(new DateOnly(2026, 9, 15), 200m, 180m, 20m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
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
    public async Task GetSummary_WeekGranularity_ReportsMondayThroughSundayAndMarksThePeriodCutByFrom()
    {
        // The filter starts on Wednesday, so the week holds only part of its days while the label
        // still names the whole week: the row must say so instead of looking like a full one.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 11, 5), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Week,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 11, 4),
                    To = new DateOnly(2026, 11, 8),
                },
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.PeriodStart.Should().Be(new DateOnly(2026, 11, 2));
        row.PeriodEnd.Should().Be(new DateOnly(2026, 11, 8));
        row.IsPartial.Should().BeTrue();
    }

    [Fact]
    public async Task GetSummary_WeekGranularity_MarksThePeriodCutByTo()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 11, 4), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Week,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 11, 2),
                    To = new DateOnly(2026, 11, 5),
                },
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.PeriodStart.Should().Be(new DateOnly(2026, 11, 2));
        row.PeriodEnd.Should().Be(new DateOnly(2026, 11, 8));
        row.IsPartial.Should().BeTrue();
    }

    [Fact]
    public async Task GetSummary_WeekGranularity_WholeWeekIsNotPartial()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 11, 4), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Week,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 11, 2),
                    To = new DateOnly(2026, 11, 8),
                },
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_DayGranularity_IsNeverPartial()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 11, 5), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Day,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 11, 3),
                    To = new DateOnly(2026, 11, 9),
                },
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.PeriodStart.Should().Be(new DateOnly(2026, 11, 5));
        row.PeriodEnd.Should().Be(new DateOnly(2026, 11, 5));
        row.IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_GroupedByNone_FillsTheMissingPeriodWithZeros()
    {
        // A trend chart must not compress a month that had no activity: the period is present with
        // zero money, while the margin stays unknown because no line carries a cost.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
            PnlTestData.Row(new DateOnly(2026, 10, 1), 200m, 180m, 20m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Month,
                GroupBy = PnlSummaryGroupBy.None,
            },
            CancellationToken.None);

        response.Rows.Select(r => r.PeriodStart).Should().ContainInOrder(
            new DateOnly(2026, 8, 1),
            new DateOnly(2026, 9, 1),
            new DateOnly(2026, 10, 1));

        PnlSummaryRowView september = response.Rows.Single(r => r.PeriodStart == new DateOnly(2026, 9, 1));

        september.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
        september.Lines.Should().Be(0);
        september.RevenueAmount.Should().Be(0m);
        september.NetRevenueAmount.Should().Be(0m);
        september.CommissionAmount.Should().Be(0m);
        september.ProductionCostAmount.Should().Be(0m);
        september.Margin.Should().BeNull();
        september.ContributionMarginPercent.Should().BeNull();
        september.RoiPercent.Should().BeNull();
        september.IsMarginComplete.Should().BeFalse();
        september.IsRoiComplete.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_GroupedByProduct_DoesNotFillMissingPeriods()
    {
        // A grouped summary stays sparse: a synthetic row cannot name the products that had no
        // activity, so filling the grid would mean inventing groups.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()),
            PnlTestData.Row(new DateOnly(2026, 10, 1), 200m, 180m, 20m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Month,
                GroupBy = PnlSummaryGroupBy.Product,
            },
            CancellationToken.None);

        response.Rows.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetSummary_TotalGranularity_ReportsTheRequestedRangeAsItsPeriod()
    {
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 10), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Total,
                GroupBy = PnlSummaryGroupBy.None,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(2026, 8, 1),
                    To = new DateOnly(2026, 9, 30),
                },
            },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.PeriodStart.Should().Be(new DateOnly(2026, 8, 1));
        row.PeriodEnd.Should().Be(new DateOnly(2026, 9, 30));
        row.IsPartial.Should().BeFalse();
    }

    [Fact]
    public async Task GetSummary_OpenRangeSpanningMoreThanThePeriodLimit_IsRejected()
    {
        // One edge comes from the data, so the validator cannot see the span; the handler measures it
        // after the fold instead of materialising eight hundred daily rows.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2020, 1, 1), 100m, 90m, 10m, Guid.NewGuid()),
            PnlTestData.Row(new DateOnly(2022, 1, 1), 100m, 90m, 10m, Guid.NewGuid()),
        };

        Func<Task> act = () => PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Day,
                GroupBy = PnlSummaryGroupBy.None,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task GetSummary_GroupedByProductOpenRangeSpanningMoreThanThePeriodLimit_IsRejected()
    {
        // The period axis is bounded for every grouping, not only for the one whose gaps are filled.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2020, 1, 1), 100m, 90m, 10m, Guid.NewGuid()),
            PnlTestData.Row(new DateOnly(2022, 1, 1), 100m, 90m, 10m, Guid.NewGuid()),
        };

        Func<Task> act = () => PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Day,
                GroupBy = PnlSummaryGroupBy.Product,
            },
            CancellationToken.None);

        await act.Should().ThrowAsync<ValidationException>();
    }

    [Fact]
    public async Task GetSummary_DayPeriodAtTheMaximumDate_DoesNotOverflow()
    {
        // The fill loop steps to the next period after the last bucket: at the end of DateOnly that
        // step must not run at all.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(9999, 12, 30), 100m, 90m, 10m, Guid.NewGuid()),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Day,
                GroupBy = PnlSummaryGroupBy.None,
                Filter = new PnlQueryFilter
                {
                    From = new DateOnly(9999, 12, 29),
                    To = new DateOnly(9999, 12, 31),
                },
            },
            CancellationToken.None);

        response.Rows.Should().HaveCount(3);
        response.Rows[^1].PeriodStart.Should().Be(new DateOnly(9999, 12, 31));
    }

    [Fact]
    public async Task GetSummary_EchoesTheGroupingDimension()
    {
        PnlSummaryResponse productGrouping = await PnlTestData.SummaryHandler(new List<PnlLineRow>())
            .Handle(new GetPnlSummaryQuery { GroupBy = PnlSummaryGroupBy.Product }, CancellationToken.None);

        PnlSummaryResponse channelGrouping = await PnlTestData.SummaryHandler(new List<PnlLineRow>())
            .Handle(new GetPnlSummaryQuery { GroupBy = PnlSummaryGroupBy.Channel }, CancellationToken.None);

        PnlSummaryResponse dimensionlessGrouping = await PnlTestData.SummaryHandler(new List<PnlLineRow>())
            .Handle(new GetPnlSummaryQuery { GroupBy = PnlSummaryGroupBy.None }, CancellationToken.None);

        productGrouping.GroupBy.Should().Be(PnlSummaryGroupBy.Product);
        channelGrouping.GroupBy.Should().Be(PnlSummaryGroupBy.Channel);
        dimensionlessGrouping.GroupBy.Should().Be(PnlSummaryGroupBy.None);
    }

    [Fact]
    public async Task GetSummary_ReportsTheLinesTheCurrencyFilterKeptOut()
    {
        // Arrange
        PnlLineRow row = PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m);

        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage(), new List<PnlLineRow> { row })
        {
            CurrencyExclusions = new List<PnlCurrencyExclusion>
            {
                new() { Currency = "EUR", Lines = 1, RevenueAmount = 40m },
                new() { Currency = "USD", Lines = 2, RevenueAmount = 250m },
            },
        };

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object, PnlTestData.Lookup().Object);

        // Act
        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Filter = new PnlQueryFilter { From = new DateOnly(2026, 8, 1) } },
            CancellationToken.None);

        // Assert
        response.Rows.Should().ContainSingle();
        response.Currency.Should().Be("RUB");

        // The rows stay single-currency, so the reader has to be told what the totals left out and
        // what filters produced that population.
        response.ExcludedByCurrency.Should().Be(3);
        response.ExcludedCurrencies.Should().HaveCount(2);
        response.ExcludedCurrencies.Should().Contain(e => e.Currency == "USD" && e.RevenueAmount == 250m);
        store.LastCurrencyFilter!.From.Should().Be(new DateOnly(2026, 8, 1));
    }

    [Fact]
    public async Task GetSummary_ProductRowsCarryTheSkuOfTheLinkedProduct()
    {
        // Arrange: two periods of the same product, so the lookup must be asked for one identifier.
        Guid productId = Guid.NewGuid();
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
            PnlTestData.Row(new DateOnly(2026, 9, 1), 200m, 180m, 20m, productId),
        };

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        lookup
            .Setup(l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string> { [productId] = "SKU-42" });

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        // Assert
        response.Rows.Should().HaveCount(2).And.OnlyContain(row => row.Sku == "SKU-42");
        lookup.Verify(
            l => l.GetSkusAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(productId)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetSummary_GroupedByChannel_DoesNotResolveSkus()
    {
        // Arrange: a channel summary has no product axis, so the catalogue must not be queried.
        Guid productId = Guid.NewGuid();
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, productId),
        };

        Mock<IProductSkuLookup> lookup = PnlTestData.Lookup();
        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            new PnlReadStoreFake(new PnlLinePage(), rows),
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            lookup.Object);

        // Act
        PnlSummaryResponse response = await handler.Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Total,
                GroupBy = PnlSummaryGroupBy.Channel,
            },
            CancellationToken.None);

        // Assert
        response.Rows.Should().ContainSingle().Which.Sku.Should().BeNull();
        lookup.Verify(
            l => l.GetSkusAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetSummary_MultipleProductIds_ArePassedToTheStore()
    {
        // Arrange
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage(), new List<PnlLineRow>());

        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        await handler.Handle(
            new GetPnlSummaryQuery
            {
                Filter = new PnlQueryFilter { ProductIds = new[] { first, second } },
                Granularity = PnlPeriodGranularity.Total,
            },
            CancellationToken.None);

        // Assert
        store.LastFilter.Should().NotBeNull();
        store.LastFilter!.ProductIds.Should().BeEquivalentTo(new[] { first, second });
    }

    [Fact]
    public async Task GetSummary_OverTheResponseBudget_IsRejectedBeforeTheWholeFold()
    {
        // Arrange: one product group per row, far above the response budget. The handler must stop
        // as soon as the budget is exceeded instead of folding the whole history.
        List<PnlLineRow> rows = new();

        for (int index = 0; index < 20_000; index++)
        {
            rows.Add(PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()));
        }

        PnlReadStoreFake store = new PnlReadStoreFake(new PnlLinePage(), rows);
        GetPnlSummaryQueryHandler handler = new GetPnlSummaryQueryHandler(
            store,
            PnlTestData.TenantContext(),
            PnlTestData.SettingsProvider().Object,
            PnlTestData.Lookup().Object);

        // Act
        Func<Task> act = () => handler.Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Total,
                GroupBy = PnlSummaryGroupBy.Product,
            },
            CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<ValidationException>()
            .WithMessage($"*{GetPnlSummaryQueryHandler.MaxResponseRows}*");

        store.PulledRows.Should().Be(GetPnlSummaryQueryHandler.MaxResponseRows + 1);
    }

    [Fact]
    public async Task GetSummary_AtTheResponseBudget_IsAllowed()
    {
        // Arrange: exactly the budget is still a valid, readable report.
        List<PnlLineRow> rows = new();

        for (int index = 0; index < GetPnlSummaryQueryHandler.MaxResponseRows; index++)
        {
            rows.Add(PnlTestData.Row(new DateOnly(2026, 8, 1), 100m, 90m, 10m, Guid.NewGuid()));
        }

        // Act
        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery
            {
                Granularity = PnlPeriodGranularity.Total,
                GroupBy = PnlSummaryGroupBy.Product,
            },
            CancellationToken.None);

        // Assert
        response.Rows.Should().HaveCount(GetPnlSummaryQueryHandler.MaxResponseRows);
    }

    [Fact]
    public async Task GetSummary_SumsTheDeductionBreakdownAndReconcilesWithNetRevenue()
    {
        // The breakdown is a column of the group, not a copy of one line: each component is summed
        // over the whole group, and the group still reconciles revenue + components = net revenue.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(
                new DateOnly(2026, 8, 1),
                revenue: 1000m,
                netRevenue: 700m,
                cost: 300m,
                commission: -250m,
                acquiring: -20m,
                deliveryFee: -30m,
                refund: 0m,
                compensation: 0m),
            PnlTestData.Row(
                new DateOnly(2026, 8, 2),
                revenue: 500m,
                netRevenue: 400m,
                cost: 100m,
                commission: -80m,
                deliveryFee: -20m),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.RevenueAmount.Should().Be(1500m);
        row.CommissionAmount.Should().Be(-330m);
        row.AcquiringAmount.Should().Be(-20m);
        row.DeliveryFeeAmount.Should().Be(-50m);
        row.RefundAmount.Should().Be(0m);
        row.CompensationAmount.Should().Be(0m);
        row.NetRevenueAmount.Should().Be(1100m);

        (row.RevenueAmount!.Value
            + row.CommissionAmount!.Value
            + row.AcquiringAmount!.Value
            + row.DeliveryFeeAmount!.Value
            + row.RefundAmount!.Value
            + row.CompensationAmount!.Value)
            .Should().Be(row.NetRevenueAmount!.Value);
    }

    [Fact]
    public async Task GetSummary_ComponentNoLineReported_StaysUnknown()
    {
        // A channel that never charges acquiring is different from one that reported a zero fee, so
        // a component absent from every line of the group must not read as a zero deduction.
        List<PnlLineRow> rows = new()
        {
            PnlTestData.Row(new DateOnly(2026, 8, 1), revenue: 1000m, netRevenue: 700m, cost: 300m, commission: -300m),
            PnlTestData.Row(new DateOnly(2026, 8, 2), revenue: 1000m, netRevenue: 800m, cost: 200m, commission: -200m),
        };

        PnlSummaryResponse response = await PnlTestData.SummaryHandler(rows).Handle(
            new GetPnlSummaryQuery { Granularity = PnlPeriodGranularity.Month },
            CancellationToken.None);

        PnlSummaryRowView row = response.Rows.Should().ContainSingle().Subject;

        row.CommissionAmount.Should().Be(-500m);
        row.AcquiringAmount.Should().BeNull();
        row.DeliveryFeeAmount.Should().BeNull();
        row.RefundAmount.Should().BeNull();
        row.CompensationAmount.Should().BeNull();
    }
}
