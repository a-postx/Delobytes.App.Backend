namespace Delobytes.App.Backend.Finance.Application.Queries;

/// <summary>Granularity of the P&amp;L summary.</summary>
public enum PnlPeriodGranularity
{
    Day = 0,
    Week = 1,
    Month = 2,

    /// <summary>
    /// The whole requested range as a single period. The reported period start is the start of the
    /// filter, so the client never has to sum the periods itself and never has to guess where the
    /// range begins.
    /// </summary>
    Total = 3,
}
