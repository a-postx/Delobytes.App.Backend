namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// Lifecycle of one expected document. An expectation is created when a sale arrives without the
/// document that always accompanies it, and is closed either by that document or by a decision.
/// </summary>
public enum PnlExpectationState
{
    /// <summary>
    /// Waiting, and the window has not elapsed yet.
    /// </summary>
    Expected = 0,

    /// <summary>
    /// Waiting, and the window elapsed without the document. Only an administrator can close it.
    /// </summary>
    Overdue = 1,

    /// <summary>
    /// The expected document arrived.
    /// </summary>
    ClosedByDocument = 2,

    /// <summary>
    /// Closed by an administrator, or auto-closed as immaterial when the estimated impact is below
    /// the materiality threshold.
    /// </summary>
    ClosedWithException = 3,
}
