namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// The business meaning of an amount reported by a channel. Only operations the product can apply
/// today are listed: anything else is discarded at ingestion rather than stored unmapped.
/// </summary>
public enum PnlEntryType
{
    /// <summary>
    /// The sale itself — what the buyer paid for the unit.
    /// </summary>
    Sale = 0,

    /// <summary>
    /// The marketplace commission, including the Wildberries sales commission and reward.
    /// </summary>
    Commission = 1,

    /// <summary>
    /// The acquiring fee charged (or compensated) by the payment provider.
    /// </summary>
    Acquiring = 2,

    /// <summary>
    /// Delivery charged by the marketplace for the unit.
    /// </summary>
    Logistics = 3,

    /// <summary>
    /// A refund granted for the unit.
    /// </summary>
    Refund = 4,

    /// <summary>
    /// The total the channel reports as remitted for the same document. Only valid together with
    /// <see cref="PnlEntryKind.ControlTotal"/>, and never summed into the result.
    /// </summary>
    Payout = 5,
}
