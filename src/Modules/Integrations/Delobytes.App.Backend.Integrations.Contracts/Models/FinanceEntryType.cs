namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// The business meaning of a finance amount. Mirrors the Finance journal type.
/// </summary>
public enum FinanceEntryType
{
    /// <summary>The sale itself.</summary>
    Sale = 0,

    /// <summary>The marketplace commission.</summary>
    Commission = 1,

    /// <summary>The acquiring fee.</summary>
    Acquiring = 2,

    /// <summary>Delivery charged by the marketplace.</summary>
    Logistics = 3,

    /// <summary>A refund granted for the unit.</summary>
    Refund = 4,

    /// <summary>The net cash movement the channel reports; valid only with <see cref="FinanceEntryKind.ControlTotal"/>.</summary>
    Payout = 5,

    /// <summary>The marketplace-funded part of the buyer's discount.</summary>
    Compensation = 6,
}
