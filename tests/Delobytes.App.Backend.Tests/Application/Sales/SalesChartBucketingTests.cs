using System;
using System.Collections.Generic;
using System.Linq;
using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Queries.Orders;
using Delobytes.App.Backend.Sales.Application.Services;
using FluentAssertions;
using Xunit;

namespace Delobytes.App.Backend.Tests.Application.Sales;

/// <summary>
/// Tests for SalesChartBucketing. The class is the only place where order-line facts become a
/// chart series, so these tests pin the three things the widget depends on: the tenant-local
/// day a fact belongs to, the adaptive/explicit bucket sizing, and the contiguous zero-filled
/// series with Monday/1st bucket starts.
/// </summary>
public class SalesChartBucketingTests
{
    private static readonly TimeZoneInfo TenantZonePlus3 = TimeZoneInfo.FindSystemTimeZoneById("Europe/Moscow");
    private static readonly TimeZoneInfo TenantZoneMinus5 = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    [Fact]
    public void GroupByLocalDay_FactLateUtcEvening_LandsOnNextDayForAheadZone()
    {
        // Arrange: 2026-01-05T23:30Z is still 2026-01-05 in UTC, but already 2026-01-06 at UTC+3.
        List<OrderLineFact> facts = new() { Fact(new DateTimeOffset(2026, 1, 5, 23, 30, 0, TimeSpan.Zero)) };

        // Act
        IReadOnlyList<OrderDailyTotal> daily = SalesChartBucketing.GroupByLocalDay(facts, TenantZonePlus3);

        // Assert
        daily.Should().ContainSingle().Which.Day.Should().Be(new DateOnly(2026, 1, 6));
    }

    [Fact]
    public void GroupByLocalDay_FactLateUtcEvening_StaysOnSameDayForBehindZone()
    {
        // Arrange: the same instant is 18:30 on 2026-01-05 at UTC-5.
        List<OrderLineFact> facts = new() { Fact(new DateTimeOffset(2026, 1, 5, 23, 30, 0, TimeSpan.Zero)) };

        // Act
        IReadOnlyList<OrderDailyTotal> daily = SalesChartBucketing.GroupByLocalDay(facts, TenantZoneMinus5);

        // Assert
        daily.Should().ContainSingle().Which.Day.Should().Be(new DateOnly(2026, 1, 5));
    }

    [Fact]
    public void GroupByLocalDay_NullUnitPrice_ContributesZeroAmountButStillCountsQuantity()
    {
        // Arrange
        List<OrderLineFact> facts = new()
        {
            Fact(new DateTimeOffset(2026, 1, 5, 10, 0, 0, TimeSpan.Zero), quantity: 1, unitPrice: null),
        };

        // Act
        IReadOnlyList<OrderDailyTotal> daily = SalesChartBucketing.GroupByLocalDay(facts, TenantZonePlus3);

        // Assert
        daily.Should().ContainSingle();
        daily[0].Quantity.Should().Be(1);
        daily[0].Amount.Should().Be(0m);
        daily[0].OrderCount.Should().Be(1);
    }

    [Fact]
    public void GroupByLocalDay_MultipleFactsSameDay_SumsQuantityAndUnitPriceTimesQuantity()
    {
        // Arrange: 2 x 100.50 + 3 x 50.25 = 351.75 on one local day.
        List<OrderLineFact> facts = new()
        {
            Fact(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero), quantity: 2, unitPrice: 100.50m),
            Fact(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero), quantity: 3, unitPrice: 50.25m),
        };

        // Act
        IReadOnlyList<OrderDailyTotal> daily = SalesChartBucketing.GroupByLocalDay(facts, TenantZonePlus3);

        // Assert
        daily.Should().ContainSingle();
        daily[0].Quantity.Should().Be(5);
        daily[0].Amount.Should().Be(351.75m);
        daily[0].OrderCount.Should().Be(2);
    }

    [Fact]
    public void GroupByLocalDay_MultipleLinesSameOrder_CountsOrderOnce()
    {
        // Arrange: two lines of the same order must count as a single order.
        Guid orderId = Guid.NewGuid();
        List<OrderLineFact> facts = new()
        {
            Fact(new DateTimeOffset(2026, 1, 5, 8, 0, 0, TimeSpan.Zero), quantity: 1, unitPrice: 100m, orderId: orderId),
            Fact(new DateTimeOffset(2026, 1, 5, 9, 0, 0, TimeSpan.Zero), quantity: 1, unitPrice: 50m, orderId: orderId),
        };

        // Act
        IReadOnlyList<OrderDailyTotal> daily = SalesChartBucketing.GroupByLocalDay(facts, TenantZonePlus3);

        // Assert
        daily.Should().ContainSingle();
        daily[0].Quantity.Should().Be(2);
        daily[0].OrderCount.Should().Be(1);
    }

    [Fact]
    public void GroupByLocalDay_EmptyInput_ReturnsEmptyList()
    {
        // Act
        IReadOnlyList<OrderDailyTotal> daily = SalesChartBucketing.GroupByLocalDay(
            new List<OrderLineFact>(),
            TenantZonePlus3);

        // Assert
        daily.Should().BeEmpty();
    }

    [Theory]
    [InlineData(31, ChartGranularity.Day)]
    [InlineData(32, ChartGranularity.Week)]
    [InlineData(120, ChartGranularity.Week)]
    [InlineData(121, ChartGranularity.Month)]
    public void ResolveAdaptiveGranularity_RangeLengthAtThreshold_ReturnsExpected(int rangeDays, ChartGranularity expected)
    {
        // Arrange
        DateOnly from = new(2026, 1, 1);
        DateOnly to = from.AddDays(rangeDays - 1);

        // Act
        ChartGranularity actual = SalesChartBucketing.ResolveAdaptiveGranularity(from, to);

        // Assert
        actual.Should().Be(expected);
    }

    [Fact]
    public void ResolveAdaptiveGranularity_SingleDay_ReturnsDay()
    {
        // Arrange
        DateOnly day = new(2026, 1, 5);

        // Act
        ChartGranularity actual = SalesChartBucketing.ResolveAdaptiveGranularity(day, day);

        // Assert
        actual.Should().Be(ChartGranularity.Day);
    }

    [Fact]
    public void GetBucketStart_SundayWeekGranularity_ReturnsPrecedingMonday()
    {
        // Arrange: 2026-01-11 is a Sunday.
        DateOnly sunday = new(2026, 1, 11);

        // Act
        DateOnly bucketStart = SalesChartBucketing.GetBucketStart(sunday, ChartGranularity.Week);

        // Assert
        bucketStart.Should().Be(new DateOnly(2026, 1, 5));
        bucketStart.DayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    [Fact]
    public void GetBucketStart_MondayWeekGranularity_ReturnsSameDay()
    {
        // Arrange: 2026-01-05 is a Monday.
        DateOnly monday = new(2026, 1, 5);

        // Act
        DateOnly bucketStart = SalesChartBucketing.GetBucketStart(monday, ChartGranularity.Week);

        // Assert
        bucketStart.Should().Be(monday);
    }

    [Fact]
    public void GetBucketStart_DayGranularity_ReturnsIdentity()
    {
        // Arrange
        DateOnly day = new(2026, 1, 7);

        // Act
        DateOnly bucketStart = SalesChartBucketing.GetBucketStart(day, ChartGranularity.Day);

        // Assert
        bucketStart.Should().Be(day);
    }

    [Fact]
    public void GetBucketStart_MonthGranularity_ReturnsFirstOfMonth()
    {
        // Arrange
        DateOnly day = new(2026, 1, 31);

        // Act
        DateOnly bucketStart = SalesChartBucketing.GetBucketStart(day, ChartGranularity.Month);

        // Assert
        bucketStart.Should().Be(new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void BuildSeries_MonthGranularityAcrossYearBoundary_EmitsThirteenContiguousBuckets()
    {
        // Arrange: 2025-12-15 .. 2026-12-01 spans the year boundary.
        DateOnly from = new(2025, 12, 15);
        DateOnly to = new(2026, 12, 1);

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            new List<OrderDailyTotal>(),
            from,
            to,
            ChartGranularity.Month);

        // Assert
        points.Should().HaveCount(13);
        points[0].PeriodStart.Should().Be(new DateOnly(2025, 12, 1));
        points[1].PeriodStart.Should().Be(new DateOnly(2026, 1, 1));
        points[^1].PeriodStart.Should().Be(new DateOnly(2026, 12, 1));
        points.Select(point => point.PeriodStart).Should().BeInAscendingOrder();
    }

    [Fact]
    public void BuildSeries_EmptyRange_IsZeroFilledNotEmpty()
    {
        // Arrange
        DateOnly from = new(2026, 1, 5);
        DateOnly to = new(2026, 1, 7);

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            new List<OrderDailyTotal>(),
            from,
            to,
            ChartGranularity.Day);

        // Assert
        points.Should().HaveCount(3);
        points.Should().OnlyContain(point => point.Quantity == 0 && point.Amount == 0m && point.OrderCount == 0);
    }

    [Fact]
    public void BuildSeries_EmptyInput_StillReturnsFullZeroSeries()
    {
        // Arrange
        DateOnly from = new(2026, 1, 5);
        DateOnly to = new(2026, 1, 11);

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            new List<OrderDailyTotal>(),
            from,
            to,
            ChartGranularity.Week);

        // Assert
        points.Should().ContainSingle();
        points[0].PeriodStart.Should().Be(new DateOnly(2026, 1, 5));
        points[0].Quantity.Should().Be(0);
        points[0].Amount.Should().Be(0m);
        points[0].OrderCount.Should().Be(0);
    }

    [Fact]
    public void BuildSeries_SingleDayRange_ProducesExactlyOnePoint()
    {
        // Arrange
        DateOnly day = new(2026, 1, 5);

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            new List<OrderDailyTotal>(),
            day,
            day,
            ChartGranularity.Day);

        // Assert
        points.Should().ContainSingle().Which.PeriodStart.Should().Be(day);
    }

    [Fact]
    public void BuildSeries_PartialEdgeWeeks_EmitBucketStartingBeforeFrom()
    {
        // Arrange: from is a Wednesday and to the Sunday of the following week.
        DateOnly from = new(2026, 1, 7);
        DateOnly to = new(2026, 1, 18);

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            new List<OrderDailyTotal>(),
            from,
            to,
            ChartGranularity.Week);

        // Assert: buckets start on 01-05 and 01-12; the first one precedes the requested from.
        points.Should().HaveCount(2);
        points[0].PeriodStart.Should().Be(new DateOnly(2026, 1, 5));
        points[1].PeriodStart.Should().Be(new DateOnly(2026, 1, 12));
    }

    [Fact]
    public void BuildSeries_TwoDaysInSameWeek_SumsIntoOneBucket()
    {
        // Arrange: both 01-06 and 01-08 belong to the week starting 01-05.
        List<OrderDailyTotal> daily = new()
        {
            Total(new DateOnly(2026, 1, 6), quantity: 2, amount: 100.10m, orderCount: 1),
            Total(new DateOnly(2026, 1, 8), quantity: 3, amount: 50.20m, orderCount: 2),
        };

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            daily,
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 11),
            ChartGranularity.Week);

        // Assert
        points.Should().ContainSingle();
        points[0].PeriodStart.Should().Be(new DateOnly(2026, 1, 5));
        points[0].Quantity.Should().Be(5);
        points[0].Amount.Should().Be(150.30m);
        points[0].OrderCount.Should().Be(3);
    }

    [Fact]
    public void BuildSeries_AmountWithFractionalParts_IsRoundedToTwoDecimals()
    {
        // Arrange: three lines of 10.005 each exceed two decimals once summed.
        List<OrderDailyTotal> daily = new()
        {
            Total(new DateOnly(2026, 1, 5), quantity: 1, amount: 10.005m),
            Total(new DateOnly(2026, 1, 6), quantity: 1, amount: 10.005m),
            Total(new DateOnly(2026, 1, 7), quantity: 1, amount: 10.005m),
        };

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            daily,
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 7),
            ChartGranularity.Week);

        // Assert
        points.Should().ContainSingle();
        points[0].Amount.Should().Be(30.02m);
        points[0].OrderCount.Should().Be(3);
    }

    [Fact]
    public void BuildSeries_DayGranularity_KeepsPerDayValuesAndOrdersAscending()
    {
        // Arrange: a gap on 01-06 must still be emitted.
        List<OrderDailyTotal> daily = new()
        {
            Total(new DateOnly(2026, 1, 5), quantity: 1, amount: 10m),
            Total(new DateOnly(2026, 1, 7), quantity: 4, amount: 40m),
        };

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            daily,
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 7),
            ChartGranularity.Day);

        // Assert
        points.Should().HaveCount(3);
        points[0].Quantity.Should().Be(1);
        points[1].Quantity.Should().Be(0);
        points[1].Amount.Should().Be(0m);
        points[2].Quantity.Should().Be(4);
        points.Select(point => point.PeriodStart).Should().BeInAscendingOrder();
    }

    [Fact]
    public void BuildSeries_DayGranularityOutsideRange_IsNotEmitted()
    {
        // Arrange: a day total outside [from, to] must not widen the series.
        List<OrderDailyTotal> daily = new()
        {
            Total(new DateOnly(2026, 1, 4), quantity: 9, amount: 900m),
            Total(new DateOnly(2026, 1, 5), quantity: 1, amount: 10m),
        };

        // Act
        IReadOnlyList<OrdersChartPoint> points = SalesChartBucketing.BuildSeries(
            daily,
            new DateOnly(2026, 1, 5),
            new DateOnly(2026, 1, 5),
            ChartGranularity.Day);

        // Assert
        points.Should().ContainSingle();
        points[0].Quantity.Should().Be(1);
        points[0].Amount.Should().Be(10m);
        points[0].OrderCount.Should().Be(1);
    }

    private static OrderLineFact Fact(
        DateTimeOffset orderDate,
        int quantity = 1,
        decimal? unitPrice = 100m,
        Guid? orderId = null)
    {
        return new OrderLineFact
        {
            OrderId = orderId ?? Guid.NewGuid(),
            OrderDate = orderDate,
            Quantity = quantity,
            UnitPrice = unitPrice,
        };
    }

    private static OrderDailyTotal Total(DateOnly day, int quantity, decimal amount, int orderCount = 1)
    {
        return new OrderDailyTotal
        {
            Day = day,
            Quantity = quantity,
            Amount = amount,
            OrderCount = orderCount,
        };
    }
}
