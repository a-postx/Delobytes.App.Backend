using Delobytes.App.Backend.Finance.Application.Queries;
using Delobytes.App.Backend.Finance.Application.Ranking;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Finance;

/// <summary>
/// PnlProductRanking, the pure ranking rule of the product dashboard. The contract: the order and
/// the 1-based rank follow the chosen metric, a row without the metric never pads the slice, partial
/// coverage can be excluded, and ties fall back to revenue and product identifier so the order is
/// stable between requests.
/// </summary>
public class PnlProductRankingTests
{
    [Fact]
    public void Rank_ByMarginDescending_OrdersAndNumbersRows()
    {
        // Arrange
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 100m, revenue: 1000m),
            Row(margin: 300m, revenue: 3000m),
            Row(margin: 200m, revenue: 2000m),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: true,
            take: 3,
            PnlRankingCoverage.Any,
            minRevenue: null);

        // Assert
        result.Rows.Select(row => row.Margin).Should().ContainInOrder(300m, 200m, 100m);
        result.Rows.Select(row => row.Rank).Should().ContainInOrder(1, 2, 3);
        result.RankedRows.Should().Be(3);
        result.TotalRows.Should().Be(3);
    }

    [Fact]
    public void Rank_AscendingDirection_OrdersFromWorstToBest()
    {
        // Arrange
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 100m, revenue: 1000m),
            Row(margin: 300m, revenue: 3000m),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: false,
            take: 2,
            PnlRankingCoverage.Any,
            minRevenue: null);

        // Assert
        result.Rows.Select(row => row.Margin).Should().ContainInOrder(100m, 300m);
    }

    [Fact]
    public void Rank_RowWithoutTheMetric_DoesNotEnterTheRanking()
    {
        // Arrange: the unknown margin is not comparable, so it must not pad a top-N list.
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 100m, revenue: 1000m),
            Row(margin: null, revenue: 5000m),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: true,
            take: 10,
            PnlRankingCoverage.Any,
            minRevenue: null);

        // Assert
        result.Rows.Should().ContainSingle().Which.Margin.Should().Be(100m);
        result.RankedRows.Should().Be(1);
        result.TotalRows.Should().Be(2);
    }

    [Fact]
    public void Rank_CompleteCoverage_ExcludesPartiallyCoveredMargins()
    {
        // Arrange: a partial margin is a real figure over part of the group, but not comparable
        // with a complete one.
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 500m, revenue: 2000m, marginComplete: false),
            Row(margin: 100m, revenue: 1000m, marginComplete: true),
        };

        // Act
        PnlRankingResult complete = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: true,
            take: 10,
            PnlRankingCoverage.Complete,
            minRevenue: null);

        PnlRankingResult any = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: true,
            take: 10,
            PnlRankingCoverage.Any,
            minRevenue: null);

        // Assert
        complete.Rows.Should().ContainSingle().Which.Margin.Should().Be(100m);
        complete.RankedRows.Should().Be(1);
        any.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Rank_CompleteCoverageByRoi_ExcludesRowsWithIncompleteInvestment()
    {
        // Arrange
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 100m, revenue: 1000m, roi: 80m, roiComplete: false),
            Row(margin: 100m, revenue: 1000m, roi: 40m, roiComplete: true),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Roi,
            descending: true,
            take: 10,
            PnlRankingCoverage.Complete,
            minRevenue: null);

        // Assert
        result.Rows.Should().ContainSingle().Which.RoiPercent.Should().Be(40m);
    }

    [Fact]
    public void Rank_MinRevenue_DropsRowsBelowTheFloor()
    {
        // Arrange: a tiny base produces an extreme percentage and must not dominate the ranking.
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 100m, revenue: 100m),
            Row(margin: 1000m, revenue: 10_000m),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: true,
            take: 10,
            PnlRankingCoverage.Any,
            minRevenue: 1000m);

        // Assert
        result.Rows.Should().ContainSingle().Which.RevenueAmount.Should().Be(10_000m);
        result.RankedRows.Should().Be(1);
    }

    [Fact]
    public void Rank_TiedMetric_FallsBackToRevenueThenProductId()
    {
        // Arrange: all three rows carry the same margin, so only the tie-break decides the order.
        Guid lowerId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        Guid higherId = Guid.Parse("00000000-0000-0000-0000-000000000002");

        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 100m, revenue: 1000m, productId: higherId),
            Row(margin: 100m, revenue: 1000m, productId: lowerId),
            Row(margin: 100m, revenue: 5000m, productId: Guid.NewGuid()),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: true,
            take: 3,
            PnlRankingCoverage.Any,
            minRevenue: null);

        // Assert: the higher revenue goes first; the equal-revenue pair follows the identifier.
        result.Rows[0].RevenueAmount.Should().Be(5000m);
        result.Rows[1].ProductId.Should().Be(lowerId);
        result.Rows[2].ProductId.Should().Be(higherId);
    }

    [Fact]
    public void Rank_TakeSmallerThanPopulation_SlicesButKeepsTheRankedCount()
    {
        // Arrange
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 100m, revenue: 1000m),
            Row(margin: 200m, revenue: 2000m),
            Row(margin: 300m, revenue: 3000m),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.Margin,
            descending: true,
            take: 2,
            PnlRankingCoverage.Any,
            minRevenue: null);

        // Assert
        result.Rows.Should().HaveCount(2);
        result.Rows.Select(row => row.Margin).Should().ContainInOrder(300m, 200m);
        result.RankedRows.Should().Be(3);
        result.TotalRows.Should().Be(3);
    }

    [Fact]
    public void Rank_ByQuantity_UsesTheQuantityAndNotTheMoney()
    {
        // Arrange
        List<PnlSummaryRowView> rows = new()
        {
            Row(margin: 1000m, revenue: 10_000m, quantitySold: 1),
            Row(margin: 10m, revenue: 100m, quantitySold: 50),
        };

        // Act
        PnlRankingResult result = PnlProductRanking.Rank(
            rows,
            PnlRankingSortKey.QuantitySold,
            descending: true,
            take: 2,
            PnlRankingCoverage.Any,
            minRevenue: null);

        // Assert
        result.Rows.Select(row => row.QuantitySold).Should().ContainInOrder(50, 1);
    }

    private static PnlSummaryRowView Row(
        decimal? margin,
        decimal? revenue,
        decimal? roi = null,
        int quantitySold = 1,
        Guid? productId = null,
        bool marginComplete = true,
        bool roiComplete = true)
    {
        return new PnlSummaryRowView
        {
            ProductId = productId,
            Margin = margin,
            RevenueAmount = revenue,
            RoiPercent = roi,
            QuantitySold = quantitySold,
            IsMarginComplete = marginComplete,
            IsRoiComplete = roiComplete,
        };
    }
}
