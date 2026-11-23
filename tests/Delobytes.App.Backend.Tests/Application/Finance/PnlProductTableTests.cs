using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Finance.Application.ProductTable;
using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Domain.Enums;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// Rules of the product table: how a summary group becomes a product row, which population each
/// segment describes, and how search, counters, summary, ordering and paging behave.
/// </summary>
public class PnlProductTableTests
{
    [Fact]
    public void Map_BagClassic_ReportsTheEconomyOfTheMock()
    {
        PnlProductRow row = PnlProductTable.Map(Summary());

        row.Lines.Should().Be(336);
        row.RevenueAmount.Should().Be(412800m);
        row.DeductionsAmount.Should().Be(-71600m);
        row.SoldCostAmount.Should().Be(172800m);
        row.Margin.Should().Be(168400m);
        row.NonBuyoutQuantity.Should().Be(20);
        row.BuyoutPercent.Should().BeApproximately(94.0828m, 0.0001m);
        row.FrozenCostAmount.Should().Be(10900m);
        row.FinalRevenuePercent.Should().BeApproximately(87.5969m, 0.0001m);
        row.MarginCoveragePercent.Should().Be(100m);
        row.CostCoverage.Should().Be(PnlProductCostCoverage.Complete);
    }

    [Fact]
    public void Map_FrozenCost_IsUnknownWhenTheMarginAndTheInvestmentCoverDifferentLines()
    {
        PnlSummaryRowView summary = Summary();
        summary.LinesWithInvestedCost = summary.LinesWithMargin - 1;

        PnlProductTable.Map(summary).FrozenCostAmount.Should().BeNull();
    }

    [Fact]
    public void Map_WithoutMargin_HasNoSoldCost()
    {
        PnlSummaryRowView summary = Summary();
        summary.Margin = null;

        PnlProductTable.Map(summary).SoldCostAmount.Should().BeNull();
    }

    [Fact]
    public void Map_WithoutOrderedUnits_HasNoBuyoutPercent()
    {
        PnlSummaryRowView summary = Summary();
        summary.QuantityOrdered = 0;
        summary.QuantitySold = 0;

        PnlProductTable.Map(summary).BuyoutPercent.Should().BeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public void Map_NonPositiveRevenue_LeavesBothRevenueSharesUnknown(int revenue)
    {
        PnlSummaryRowView summary = Summary();
        summary.RevenueAmount = revenue;

        PnlProductRow row = PnlProductTable.Map(summary);

        row.MarginCoveragePercent.Should().BeNull();
        row.FinalRevenuePercent.Should().BeNull();
    }

    [Fact]
    public void CostCoverage_PositiveNetRevenueWithoutCostAndNoMargin_IsMissing()
    {
        PnlSummaryRowView summary = Summary();
        summary.NetRevenueWithoutCostAmount = 100m;
        summary.Margin = null;

        PnlProductTable.ResolveCostCoverage(summary).Should().Be(PnlProductCostCoverage.Missing);
    }

    [Fact]
    public void CostCoverage_PositiveNetRevenueWithoutCostWithAMargin_IsPartial()
    {
        PnlSummaryRowView summary = Summary();
        summary.NetRevenueWithoutCostAmount = 100m;

        PnlProductTable.ResolveCostCoverage(summary).Should().Be(PnlProductCostCoverage.Partial);
    }

    [Fact]
    public void CostCoverage_NoPositiveNetRevenueWithoutCost_IsComplete()
    {
        PnlProductTable.ResolveCostCoverage(Summary()).Should().Be(PnlProductCostCoverage.Complete);
    }

    [Fact]
    public void CostCoverage_FeeOnlyLine_IsComplete()
    {
        // A line that never sold carries a delivery charge and no cost of goods. There is nothing
        // the margin fails to cover, so the product is complete rather than missing a cost.
        PnlSummaryRowView summary = Summary();
        summary.RevenueAmount = null;
        summary.NetRevenueAmount = -54m;
        summary.NetRevenueWithoutCostAmount = -54m;
        summary.Margin = null;

        PnlProductTable.ResolveCostCoverage(summary).Should().Be(PnlProductCostCoverage.Complete);
    }

    [Fact]
    public void ApplySearch_IsCaseInsensitiveSubstring()
    {
        List<PnlProductRow> rows = new()
        {
            Product(sku: "BAG-CLASSIC-BLK"),
            Product(sku: "WALLET-SLIM-BRN"),
        };

        PnlProductTable.ApplySearch(rows, "bag")
            .Should().ContainSingle().Which.Sku.Should().Be("BAG-CLASSIC-BLK");
    }

    [Fact]
    public void ApplySearch_NullSku_NeverMatches()
    {
        List<PnlProductRow> rows = new()
        {
            Product(sku: null),
            Product(sku: "BAG-CLASSIC-BLK"),
        };

        PnlProductTable.ApplySearch(rows, "bag")
            .Should().ContainSingle().Which.Sku.Should().Be("BAG-CLASSIC-BLK");
    }

    [Fact]
    public void ApplySearch_BlankTerm_KeepsEveryRow()
    {
        List<PnlProductRow> rows = new()
        {
            Product(sku: null),
            Product(sku: "BAG-CLASSIC-BLK"),
        };

        PnlProductTable.NormalizeSearch("   ").Should().BeNull();
        PnlProductTable.ApplySearch(rows, PnlProductTable.NormalizeSearch("   "))
            .Should().HaveCount(2);
    }

    [Fact]
    public void CountSegments_AfterSearch_CountsBothSegmentsOfAPartialLoss()
    {
        List<PnlProductRow> rows = new()
        {
            Product(sku: "BAG-LOSS", margin: -5m, costCoverage: PnlProductCostCoverage.Partial),
            Product(sku: "WALLET-LOSS", margin: -7m, costCoverage: PnlProductCostCoverage.Complete),
            Product(sku: "BELT-PROFIT", margin: 10m, costCoverage: PnlProductCostCoverage.Partial),
            Product(sku: "CARD-PROFIT", margin: 20m, costCoverage: PnlProductCostCoverage.Complete),
        };

        List<PnlProductRow> searched = PnlProductTable.ApplySearch(rows, "loss");
        PnlProductSegmentCounts counts = PnlProductTable.CountSegments(searched);

        counts.All.Should().Be(2);
        counts.Loss.Should().Be(2);
        counts.WithoutCost.Should().Be(1);
    }

    [Fact]
    public void Summarize_IsIndependentOfSearchAndPaging()
    {
        List<PnlProductRow> rows = new()
        {
            Product(sku: "BAG-LOSS", margin: -5m, costCoverage: PnlProductCostCoverage.Partial,
                netRevenueWithoutCost: 50m, percent: 10m),
            Product(sku: "WALLET-LOSS", margin: -7m, costCoverage: PnlProductCostCoverage.Complete,
                percent: 20m),
            Product(sku: "BELT-NOCOST", margin: null, costCoverage: PnlProductCostCoverage.Missing,
                netRevenueWithoutCost: 70m),
        };

        PnlProductSummary summary = PnlProductTable.Summarize(rows);

        summary.Products.Should().Be(3);
        summary.LossProducts.Should().Be(2);
        summary.LossMarginAmount.Should().Be(-12m);
        summary.WithoutCostProducts.Should().Be(2);
        summary.WithoutCostNetRevenueAmount.Should().Be(120m);

        // Only the complete product enters the median: the partial one describes part of its own
        // revenue and the product without a cost carries no percent at all.
        summary.MedianContributionMarginPercent.Should().Be(20m);
    }

    [Fact]
    public void Summarize_MedianOfAnOddSet_IsTheMiddleValue()
    {
        List<PnlProductRow> rows = new()
        {
            CompleteProduct(30m),
            CompleteProduct(10m),
            CompleteProduct(20m),
        };

        PnlProductTable.Summarize(rows).MedianContributionMarginPercent.Should().Be(20m);
    }

    [Fact]
    public void Summarize_MedianOfAnEvenSet_IsTheMeanOfTheTwoMiddleValues()
    {
        List<PnlProductRow> rows = new()
        {
            CompleteProduct(40m),
            CompleteProduct(10m),
            CompleteProduct(30m),
            CompleteProduct(20m),
        };

        PnlProductTable.Summarize(rows).MedianContributionMarginPercent.Should().Be(25m);
    }

    [Fact]
    public void Summarize_NoCompleteProduct_HasNoMedian()
    {
        List<PnlProductRow> rows = new()
        {
            Product(costCoverage: PnlProductCostCoverage.Partial, percent: 40m),
            Product(costCoverage: PnlProductCostCoverage.Missing),
            Product(costCoverage: PnlProductCostCoverage.Complete, percent: null),
        };

        PnlProductTable.Summarize(rows).MedianContributionMarginPercent.Should().BeNull();
        PnlProductTable.Summarize(new List<PnlProductRow>()).MedianContributionMarginPercent
            .Should().BeNull();
    }

    [Theory]
    [InlineData(PnlProductSortKey.Revenue)]
    [InlineData(PnlProductSortKey.Deductions)]
    [InlineData(PnlProductSortKey.NetRevenue)]
    [InlineData(PnlProductSortKey.SoldCost)]
    [InlineData(PnlProductSortKey.Margin)]
    [InlineData(PnlProductSortKey.ContributionMarginPercent)]
    [InlineData(PnlProductSortKey.ReturnOnSoldCostPercent)]
    [InlineData(PnlProductSortKey.QuantitySold)]
    [InlineData(PnlProductSortKey.BuyoutPercent)]
    public void Sort_PutsRowsWithoutTheMetricLastInBothDirections(PnlProductSortKey sortBy)
    {
        List<PnlProductRow> rows = SortFixture();

        List<PnlProductRow> ascending = PnlProductTable.Sort(rows, sortBy, descending: false);
        List<PnlProductRow> descending = PnlProductTable.Sort(rows, sortBy, descending: true);

        AssertNullsLast(ascending, sortBy);
        AssertNullsLast(descending, sortBy);

        List<decimal> ascValues = Values(ascending, sortBy);
        List<decimal> descValues = Values(descending, sortBy);

        ascValues.Should().BeInAscendingOrder();
        descValues.Should().BeInDescendingOrder();
    }

    [Fact]
    public void Sort_TiesFallBackToRevenueAndThenToProductId()
    {
        Guid first = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid second = Guid.Parse("00000000-0000-0000-0000-000000000002");

        List<PnlProductRow> rows = new()
        {
            Product(productId: second, margin: 10m, revenue: 100m),
            Product(productId: first, margin: 10m, revenue: 100m),
            Product(productId: Guid.NewGuid(), margin: 10m, revenue: 300m),
        };

        List<PnlProductRow> sorted = PnlProductTable.Sort(
            rows,
            PnlProductSortKey.Margin,
            descending: true);

        sorted[0].RevenueAmount.Should().Be(300m);
        sorted[1].ProductId.Should().Be(first);
        sorted[2].ProductId.Should().Be(second);
    }

    [Fact]
    public void Sort_BySku_IsOrdinalIgnoreCaseAndKeepsNullLast()
    {
        List<PnlProductRow> rows = new()
        {
            Product(sku: "sku-2"),
            Product(sku: "SKU-10"),
            Product(sku: "sku-1"),
            Product(sku: null),
        };

        List<PnlProductRow> ascending = PnlProductTable.Sort(
            rows,
            PnlProductSortKey.Sku,
            descending: false);

        ascending.Select(row => row.Sku).Should().ContainInOrder("sku-1", "SKU-10", "sku-2", null);

        List<PnlProductRow> descending = PnlProductTable.Sort(
            rows,
            PnlProductSortKey.Sku,
            descending: true);

        descending.Select(row => row.Sku).Should().ContainInOrder("sku-2", "SKU-10", "sku-1", null);
    }

    [Fact]
    public void Slice_MiddlePage_ReturnsTheRequestedWindow()
    {
        List<PnlProductRow> rows = Enumerable.Range(0, 10)
            .Select(index => Product(sku: $"SKU-{index}"))
            .ToList();

        (List<PnlProductRow> items, int page) = PnlProductTable.Slice(rows, page: 2, pageSize: 3);

        page.Should().Be(2);
        items.Should().HaveCount(3);
        items[0].Sku.Should().Be("SKU-3");
    }

    [Fact]
    public void Slice_PageBeyondTheLast_IsClampedToTheLastPage()
    {
        List<PnlProductRow> rows = Enumerable.Range(0, 7)
            .Select(index => Product(sku: $"SKU-{index}"))
            .ToList();

        (List<PnlProductRow> items, int page) = PnlProductTable.Slice(rows, page: 99, pageSize: 5);

        page.Should().Be(2);
        items.Should().HaveCount(2);
    }

    [Fact]
    public void Slice_AnEmptyList_ServesPageOne()
    {
        (List<PnlProductRow> items, int page) = PnlProductTable.Slice(
            new List<PnlProductRow>(),
            page: 4,
            pageSize: 25);

        page.Should().Be(1);
        items.Should().BeEmpty();
    }

    private static void AssertNullsLast(List<PnlProductRow> rows, PnlProductSortKey sortBy)
    {
        int firstNull = rows.FindIndex(row => Metric(row, sortBy) == null);

        if (firstNull < 0)
        {
            return;
        }

        rows.Skip(firstNull).Should().OnlyContain(row => Metric(row, sortBy) == null);
    }

    private static List<decimal> Values(List<PnlProductRow> rows, PnlProductSortKey sortBy)
    {
        return rows
            .Select(row => Metric(row, sortBy))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToList();
    }

    private static decimal? Metric(PnlProductRow row, PnlProductSortKey sortBy)
    {
        return sortBy switch
        {
            PnlProductSortKey.Revenue => row.RevenueAmount,
            PnlProductSortKey.Deductions => row.DeductionsAmount,
            PnlProductSortKey.NetRevenue => row.NetRevenueAmount,
            PnlProductSortKey.SoldCost => row.SoldCostAmount,
            PnlProductSortKey.Margin => row.Margin,
            PnlProductSortKey.ContributionMarginPercent => row.ContributionMarginPercent,
            PnlProductSortKey.ReturnOnSoldCostPercent => row.ReturnOnSoldCostPercent,
            PnlProductSortKey.QuantitySold => row.QuantitySold,
            PnlProductSortKey.BuyoutPercent => row.BuyoutPercent,
            _ => null,
        };
    }

    private static List<PnlProductRow> SortFixture()
    {
        return new List<PnlProductRow>
        {
            Product(sku: "A", revenue: 1000m, deductions: -100m, netRevenue: 900m, soldCost: 500m,
                margin: 400m, percent: 44m, returnOnSoldCost: 80m, quantitySold: 10, buyoutPercent: 90m),
            Product(sku: "B", revenue: 500m, deductions: -50m, netRevenue: 450m, soldCost: 250m,
                margin: 200m, percent: 44m, returnOnSoldCost: 80m, quantitySold: 5, buyoutPercent: 80m),
            Product(sku: "C", revenue: 2000m, deductions: -200m, netRevenue: 1800m, soldCost: null,
                margin: null, percent: null, returnOnSoldCost: null, quantitySold: 20, buyoutPercent: null),
            Product(sku: "D", revenue: 3000m, deductions: null, netRevenue: null, soldCost: null,
                margin: null, percent: null, returnOnSoldCost: null, quantitySold: 30, buyoutPercent: null),
        };
    }

    private static PnlProductRow CompleteProduct(decimal percent)
    {
        return Product(costCoverage: PnlProductCostCoverage.Complete, percent: percent);
    }

    private static PnlProductRow Product(
        Guid? productId = null,
        string? sku = "SKU",
        decimal? revenue = 100m,
        decimal? deductions = -10m,
        decimal? netRevenue = 90m,
        decimal? soldCost = null,
        decimal? margin = 10m,
        decimal? percent = 11m,
        decimal? returnOnSoldCost = 12m,
        int quantitySold = 1,
        decimal? buyoutPercent = 100m,
        PnlProductCostCoverage costCoverage = PnlProductCostCoverage.Complete,
        decimal netRevenueWithoutCost = 0m)
    {
        return new PnlProductRow
        {
            ProductId = productId ?? Guid.NewGuid(),
            Sku = sku,
            RevenueAmount = revenue,
            DeductionsAmount = deductions,
            NetRevenueAmount = netRevenue,
            SoldCostAmount = soldCost,
            Margin = margin,
            ContributionMarginPercent = percent,
            ReturnOnSoldCostPercent = returnOnSoldCost,
            QuantitySold = quantitySold,
            BuyoutPercent = buyoutPercent,
            CostCoverage = costCoverage,
            NetRevenueWithoutCostAmount = netRevenueWithoutCost,
        };
    }

    private static PnlSummaryRowView Summary()
    {
        return new PnlSummaryRowView
        {
            ProductId = Guid.Parse("6f1c2a9e-0000-0000-0000-000000000001"),
            Lines = 336,
            RevenueAmount = 412800m,
            CommissionAmount = -57600m,
            DeliveryFeeAmount = -24800m,
            RefundAmount = -7300m,
            AcquiringAmount = -3300m,
            CompensationAmount = 21400m,
            NetRevenueAmount = 341200m,
            ProductionCostAmount = 172800m,
            Margin = 168400m,
            ContributionMarginPercent = 49.3552m,
            ReturnOnSoldCostPercent = 97.4537m,
            QuantityOrdered = 338,
            QuantitySold = 318,
            LinesWithMargin = 1,
            RevenueWithMarginAmount = 412800m,
            ProductionCostWithMarginAmount = 172800m,
            NetRevenueWithMarginAmount = 341200m,
            NetRevenueWithoutCostAmount = 0m,
            RevenueWithoutCostAmount = 0m,
            InvestedCostAmount = 183700m,
            LinesWithInvestedCost = 1,
            IsMarginComplete = true,
            ProvisionalRevenueAmount = 51200m,
            OverdueRevenueAmount = 12400m,
            ControlDifferenceAmount = -640m,
            CompletenessState = PnlLineCompleteness.Overdue,
        };
    }
}
