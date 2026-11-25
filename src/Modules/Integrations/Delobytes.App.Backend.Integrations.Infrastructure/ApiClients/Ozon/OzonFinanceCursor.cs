using System.Globalization;

namespace Delobytes.App.Backend.Integrations.Infrastructure.ApiClients.Ozon;

/// <summary>
/// Position inside the per-day finance accrual walk: the day being read plus the page id within it.
/// The day has to travel in the cursor, or a multi-day window would import only its first day.
/// </summary>
internal readonly struct OzonFinanceCursor
{
    private const string DayFormat = "yyyy-MM-dd";
    private const char Separator = '|';

    public OzonFinanceCursor(DateOnly day, string lastId)
    {
        Day = day;
        LastId = lastId;
    }

    public DateOnly Day { get; }

    public string LastId { get; }

    /// <summary>
    /// Splits a cursor into the day it belongs to and the page id within that day. A cursor carrying
    /// no day — an old in-flight one, or a hand-written page id — is read as a page id of the
    /// window's first day, which is what the previous cursor format meant.
    /// </summary>
    public static OzonFinanceCursor Parse(string? cursor, DateOnly windowFrom)
    {
        string value = cursor ?? string.Empty;

        if (value.Length == 0)
        {
            return new OzonFinanceCursor(windowFrom, string.Empty);
        }

        int separator = value.IndexOf(Separator, StringComparison.Ordinal);

        if (separator <= 0)
        {
            return new OzonFinanceCursor(windowFrom, value);
        }

        string dayPart = value[..separator];
        string lastId = value[(separator + 1)..];

        if (!DateOnly.TryParseExact(
                dayPart,
                DayFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly day))
        {
            return new OzonFinanceCursor(windowFrom, value);
        }

        return new OzonFinanceCursor(day < windowFrom ? windowFrom : day, lastId);
    }

    public override string ToString()
    {
        return string.Concat(
            Day.ToString(DayFormat, CultureInfo.InvariantCulture),
            Separator,
            LastId);
    }
}
