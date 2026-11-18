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
    /// The net cash movement the channel reports for the same document: the payout of a sale or the
    /// charge of a delivery row. Only valid together with <see cref="PnlEntryKind.ControlTotal"/>,
    /// and never summed into the result. The direction carries the sign, so a payout entry may be an
    /// <see cref="PnlEntryDirection.Expense"/> when the document reports money leaving the seller.
    /// </summary>
    Payout = 5,

    /// <summary>
    /// The marketplace-funded part of the buyer's discount: money the channel adds on top of what the
    /// buyer paid. It is revenue for the seller, so it is summed into the net revenue as its own
    /// component.
    /// </summary>
    Compensation = 6,
}
