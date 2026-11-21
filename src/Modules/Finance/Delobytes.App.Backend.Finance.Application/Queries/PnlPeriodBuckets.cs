namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>
/// Calendar periods of the P&amp;L summary. The fold, the gap filling and the request validation use
/// this one rule, so a period label cannot mean one thing to the reader and another to the limiter.
/// </summary>
public static class PnlPeriodBuckets
{
    /// <summary>
    /// Maximum number of periods one summary request may span at its granularity: a year of days,
    /// seven years of weeks or thirty years of months. It bounds the period axis; the number of
    /// groups on the other axis is bounded separately.
    /// </summary>
    public const int MaxPeriods = 366;

    /// <summary>
    /// Returns the first day of the period that contains <paramref name="date"/>: the day itself, the
    /// Monday of its ISO week, or the 1st of its month. A total period has no calendar start and is
    /// keyed by <see cref="DateOnly.MinValue"/>, which must never surface in a payload.
    /// </summary>
    public static DateOnly StartOf(DateOnly date, PnlPeriodGranularity granularity)
    {
        return granularity switch
        {
            PnlPeriodGranularity.Total => DateOnly.MinValue,
            PnlPeriodGranularity.Week => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
            PnlPeriodGranularity.Month => new DateOnly(date.Year, date.Month, 1),
            _ => date,
        };
    }

    /// <summary>Returns the last day of the period that starts on <paramref name="periodStart"/>.</summary>
    public static DateOnly EndOf(DateOnly periodStart, PnlPeriodGranularity granularity)
    {
        return granularity switch
        {
            PnlPeriodGranularity.Week => periodStart.AddDays(6),
            PnlPeriodGranularity.Month => periodStart.AddMonths(1).AddDays(-1),
            _ => periodStart,
        };
    }

    /// <summary>Returns the first day of the period that follows the one starting on <paramref name="periodStart"/>.</summary>
    public static DateOnly Next(DateOnly periodStart, PnlPeriodGranularity granularity)
    {
        return granularity switch
        {
            PnlPeriodGranularity.Week => periodStart.AddDays(7),
            PnlPeriodGranularity.Month => periodStart.AddMonths(1),
            _ => periodStart.AddDays(1),
        };
    }

    /// <summary>
    /// Counts the periods the range <paramref name="from"/>..<paramref name="to"/> touches. A total
    /// period is always one; a reversed range yields a non-positive count and is rejected by the
    /// order rule, not by the limit.
    /// </summary>
    public static int Count(DateOnly from, DateOnly to, PnlPeriodGranularity granularity)
    {
        return granularity switch
        {
            PnlPeriodGranularity.Total => 1,
            PnlPeriodGranularity.Week =>
                ((StartOf(to, granularity).DayNumber - StartOf(from, granularity).DayNumber) / 7) + 1,
            PnlPeriodGranularity.Month => ((to.Year - from.Year) * 12) + (to.Month - from.Month) + 1,
            _ => to.DayNumber - from.DayNumber + 1,
        };
    }
}
