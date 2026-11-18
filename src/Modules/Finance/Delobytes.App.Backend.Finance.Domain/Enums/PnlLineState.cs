namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// Whether the journal holds anything for a P&amp;L line. There is no "waiting" state: a line with
/// no entries is not closed yet, and whether money will ever arrive is a property of the Sales line
/// rather than of the aggregate.
/// </summary>
public enum PnlLineState
{
    /// <summary>
    /// The journal holds no entries for the line, or does not exist for it at all.
    /// </summary>
    NotCalculated = 0,

    /// <summary>
    /// The totals were computed from at least one component entry.
    /// </summary>
    Calculated = 2,
}
