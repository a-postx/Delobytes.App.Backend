namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Ordering keys the P&amp;L line page supports. The set is closed on purpose: every key must have
/// a keyset predicate, so a new key is a deliberate change of the paging contract.
/// </summary>
public enum PnlLineSortKey
{
    /// <summary>Orders by the date the line is reported under.</summary>
    Date = 0,

    /// <summary>Orders by the margin before tax, which is unknown while the cost is.</summary>
    Margin = 1,
}
