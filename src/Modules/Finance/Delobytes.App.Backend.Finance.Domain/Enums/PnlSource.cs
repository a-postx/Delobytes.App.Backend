namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// The external document a P&amp;L entry was read from. The source decides how
/// <see cref="Entities.PnlEntry.ReportInstanceKey"/> and
/// <see cref="Entities.PnlEntry.ExternalEntryId"/> are interpreted.
/// </summary>
public enum PnlSource
{
    /// <summary>
    /// A row of the Wildberries weekly detailed sales report. Rows are re-issued with the report,
    /// so the identity includes the report instance.
    /// </summary>
    WbReport = 0,

    /// <summary>
    /// A daily accrual reported by Ozon. Accruals are never re-issued: the accrual id is stable and
    /// a reversal arrives as its own accrual.
    /// </summary>
    OzonAccrual = 1,
}
