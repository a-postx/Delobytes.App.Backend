using Delobytes.App.Backend.Sales.Application.Models;
using Delobytes.App.Backend.Sales.Application.Queries.Orders;

namespace Delobytes.App.Backend.Sales.Application.Services;

/// <summary>
/// Pure, deterministic bucketing of order-line facts into the orders chart series. No EF Core,
/// no HTTP, no ambient clock: the tenant time zone is always supplied by the caller.
/// </summary>
public static class SalesChartBucketing
{
    private const int AdaptiveDayThresholdDays = 31;
    private const int AdaptiveWeekThresholdDays = 120;

    /// <summary>
    /// Converts each fact's UTC <see cref="OrderLineFact.OrderDate"/> to the tenant-local
    /// calendar day and sums <see cref="OrderLineFact.Quantity"/> and
    /// <c>UnitPrice * Quantity</c> per day. A null <see cref="OrderLineFact.UnitPrice"/>
    /// contributes 0 to the amount but the line still counts towards the quantity. The number
    /// of distinct <see cref="OrderLineFact.OrderId"/> values per day is also counted so the
    /// caller can derive the average order amount.
    /// </summary>
    /// <param name="facts">Flat per-line facts, already filtered by currency and cancellation.</param>
    /// <param name="timeZone">Tenant IANA time zone used to resolve the local calendar day.</param>
    public static IReadOnlyList<OrderDailyTotal> GroupByLocalDay(
        IReadOnlyList<OrderLineFact> facts,
        TimeZoneInfo timeZone)
    {
        return facts
            .GroupBy(fact => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fact.OrderDate, timeZone).DateTime))
            .Select(group => new OrderDailyTotal
            {
                Day = group.Key,
                Quantity = group.Sum(fact => fact.Quantity),
                Amount = group.Sum(fact => (fact.UnitPrice ?? 0m) * fact.Quantity),
                OrderCount = group.Select(fact => fact.OrderId).Distinct().Count(),
            })
            .ToList();
    }

    /// <summary>
    /// Picks a granularity from the range length when the client did not choose one:
    /// up to 31 days -> <see cref="ChartGranularity.Day"/>, up to 120 -> <see cref="ChartGranularity.Week"/>,
    /// otherwise <see cref="ChartGranularity.Month"/>.
    /// </summary>
    public static ChartGranularity ResolveAdaptiveGranularity(DateOnly from, DateOnly to)
    {
        int rangeDays = to.DayNumber - from.DayNumber + 1;

        if (rangeDays <= AdaptiveDayThresholdDays)
        {
            return ChartGranularity.Day;
        }

        if (rangeDays <= AdaptiveWeekThresholdDays)
        {
            return ChartGranularity.Week;
        }

        return ChartGranularity.Month;
    }

    /// <summary>
    /// Returns the start of the bucket that contains <paramref name="day"/>: the day itself for
    /// <see cref="ChartGranularity.Day"/>, the Monday of its ISO week for
    /// <see cref="ChartGranularity.Week"/>, or the 1st of its month for
    /// <see cref="ChartGranularity.Month"/>.
    /// </summary>
    public static DateOnly GetBucketStart(DateOnly day, ChartGranularity granularity)
    {
        switch (granularity)
        {
            case ChartGranularity.Day:
                return day;
            case ChartGranularity.Week:
                int daysSinceMonday = ((int)day.DayOfWeek + 6) % 7;
                return day.AddDays(-daysSinceMonday);
            case ChartGranularity.Month:
                return new DateOnly(day.Year, day.Month, 1);
            default:
                throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Неизвестная гранулярность.");
        }
    }

    /// <summary>
    /// Rolls <paramref name="dailyTotals"/> up into <paramref name="granularity"/> buckets and
    /// emits every bucket from the one containing <paramref name="from"/> through the one
    /// containing <paramref name="to"/>, ascending, zero-filled where no day contributed.
    /// Buckets at the edges of the range may start before <paramref name="from"/>: only the
    /// in-range days that are present in <paramref name="dailyTotals"/> contribute to them.
    /// </summary>
    public static IReadOnlyList<OrdersChartPoint> BuildSeries(
        IReadOnlyList<OrderDailyTotal> dailyTotals,
        DateOnly from,
        DateOnly to,
        ChartGranularity granularity)
    {
        Dictionary<DateOnly, OrdersChartPoint> buckets = dailyTotals
            .GroupBy(daily => GetBucketStart(daily.Day, granularity))
            .ToDictionary(
                group => group.Key,
                group => new OrdersChartPoint
                {
                    PeriodStart = group.Key,
                    Quantity = group.Sum(daily => daily.Quantity),
                    Amount = decimal.Round(group.Sum(daily => daily.Amount), 2),
                    OrderCount = group.Sum(daily => daily.OrderCount),
                });

        List<OrdersChartPoint> points = new();
        DateOnly bucketStart = GetBucketStart(from, granularity);
        DateOnly lastBucketStart = GetBucketStart(to, granularity);

        while (bucketStart <= lastBucketStart)
        {
            points.Add(buckets.TryGetValue(bucketStart, out OrdersChartPoint? existing)
                ? existing
                : new OrdersChartPoint { PeriodStart = bucketStart, Quantity = 0, Amount = 0m, OrderCount = 0 });

            bucketStart = GetNextBucketStart(bucketStart, granularity);
        }

        return points;
    }

    private static DateOnly GetNextBucketStart(DateOnly bucketStart, ChartGranularity granularity)
    {
        return granularity switch
        {
            ChartGranularity.Day => bucketStart.AddDays(1),
            ChartGranularity.Week => bucketStart.AddDays(7),
            ChartGranularity.Month => bucketStart.AddMonths(1),
            _ => throw new ArgumentOutOfRangeException(nameof(granularity), granularity, "Неизвестная гранулярность."),
        };
    }
}
