namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// What a P&amp;L entry represents in the arithmetic of a line.
/// </summary>
public enum PnlEntryKind
{
    /// <summary>
    /// A component of the line's result. Components are summed into
    /// <see cref="Entities.PnlLine.NetRevenueAmount"/>.
    /// </summary>
    Component = 0,

    /// <summary>
    /// The total the channel reports for the same document, used only to check the components
    /// against it. Never summed into the result: a payout already covers the commission, the
    /// acquiring fee and the delivery fee, so adding it would count them twice.
    /// </summary>
    ControlTotal = 1,
}
