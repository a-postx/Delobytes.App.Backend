namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// Whether every document the sale is expected to produce has arrived. Distinct from
/// <see cref="PnlLineState"/>, which only answers whether any money arrived at all: a line can hold
/// a sale and still be missing the delivery document that belongs to the same unit.
/// </summary>
public enum PnlLineCompleteness
{
    /// <summary>
    /// Every expected document arrived, or none was expected.
    /// </summary>
    Complete = 0,

    /// <summary>
    /// At least one expected document has not arrived yet, and the expectation window is still open.
    /// A margin computed here is provisional and must not be reported as final.
    /// </summary>
    Provisional = 1,

    /// <summary>
    /// An expected document is past its window and no one has closed the exception yet.
    /// </summary>
    Overdue = 2,

    /// <summary>
    /// The missing document was closed by an administrator, or auto-closed as immaterial. The
    /// amounts are final as far as this module knows, with a recorded exception behind them.
    /// </summary>
    ClosedWithException = 3,
}
