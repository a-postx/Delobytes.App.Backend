namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// Which way an amount moves the line's result. Stored separately from the amount because the
/// sign in a channel payload is not a reliable indicator: Wildberries reports the acquiring fee as
/// a positive compensation on one row and as a charge on another.
/// </summary>
public enum PnlEntryDirection
{
    /// <summary>
    /// Money the seller receives.
    /// </summary>
    Income = 0,

    /// <summary>
    /// Money the seller is charged.
    /// </summary>
    Expense = 1,
}
