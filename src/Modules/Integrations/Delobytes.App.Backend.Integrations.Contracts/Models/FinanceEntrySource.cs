namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// The external document a finance entry was read from. Mirrors the Finance journal source.
/// </summary>
public enum FinanceEntrySource
{
    /// <summary>A row of the Wildberries weekly detailed sales report.</summary>
    WbReport = 0,

    /// <summary>A daily accrual reported by Ozon.</summary>
    OzonAccrual = 1,
}
