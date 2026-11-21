using Delobytes.App.Backend.Finance.Application.Queries;

namespace Delobytes.App.Backend.Finance.Application.Aggregation;

/// <summary>
/// Turns folded groups into summary rows: resolves the period label of each group and orders the
/// rows the way the report is read. Period semantics live here, apart from accumulation, so a
/// ranking can reuse the same projection.
/// </summary>
public static class PnlSummaryProjector
{
    public static List<PnlSummaryRowView> BuildRows(
        IReadOnlyList<PnlSummaryGroup> groups,
        PnlSummaryGroupBy groupBy,
        PnlPeriodGranularity granularity,
        DateOnly? from,
        DateOnly? to)
    {
        return groups
            .Select(group =>
            {
                DateOnly periodStart = ResolvePeriodStart(granularity, from, group);
                DateOnly periodEnd = ResolvePeriodEnd(granularity, to, group, periodStart);

                return group.Build(
                    groupBy,
                    periodStart,
                    periodEnd,
                    IsPartial(granularity, from, to, periodStart, periodEnd));
            })
            .OrderBy(row => row.PeriodStart)
            .ThenBy(row => row.ProductId ?? row.ChannelId)
            .ToList();
    }

    // A total-period row reports the start of the requested range. Without a range the earliest
    // period date of the group is the only honest answer: the grouping sentinel is an internal key
    // and must not leak into the payload as a real date.
    private static DateOnly ResolvePeriodStart(
        PnlPeriodGranularity granularity,
        DateOnly? from,
        PnlSummaryGroup group)
    {
        if (granularity != PnlPeriodGranularity.Total)
        {
            return group.PeriodStart;
        }

        return from
            ?? group.EarliestPeriodDate
            ?? group.PeriodStart;
    }

    // A total-period row covers the requested range, so its end is the end of the filter and falls
    // back to the latest period date folded into the group when the range is open.
    private static DateOnly ResolvePeriodEnd(
        PnlPeriodGranularity granularity,
        DateOnly? to,
        PnlSummaryGroup group,
        DateOnly periodStart)
    {
        if (granularity == PnlPeriodGranularity.Total)
        {
            return to
                ?? group.LatestPeriodDate
                ?? periodStart;
        }

        return PnlPeriodBuckets.EndOf(periodStart, granularity);
    }

    // A period is partial only when a filter bound cuts it: the days outside the range are missing
    // while the label still describes the whole period. A day period is atomic, and a total-period
    // row is the range itself.
    private static bool IsPartial(
        PnlPeriodGranularity granularity,
        DateOnly? from,
        DateOnly? to,
        DateOnly periodStart,
        DateOnly periodEnd)
    {
        if (granularity == PnlPeriodGranularity.Total)
        {
            return false;
        }

        return (from.HasValue && periodStart < from.Value)
            || (to.HasValue && periodEnd > to.Value);
    }
}
