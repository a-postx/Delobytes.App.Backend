namespace Delobytes.App.Backend.Sales.Domain.Enums;

/// <summary>
/// Whether the money on a settlement is resolvable, still expected, or currently calculable.
/// </summary>
public enum SettlementState
{
    /// <summary>
    /// Insufficient inputs that this channel will never provide — for example no commission in the
    /// order API. This is a terminal state, not a failure.
    /// </summary>
    NotCalculated = 0,

    /// <summary>
    /// Inputs are expected later, from a financial report or a subsequent call.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// All inputs required for this entity's scope are present.
    /// </summary>
    Calculated = 2,
}
