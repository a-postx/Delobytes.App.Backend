namespace Delobytes.App.Backend.Sales.Contracts.Events;

/// <summary>
/// Published by Sales after an import registered or corrected order lines. Consumed by Finance to
/// maintain its order line projection and by Catalog to resolve production costs.
/// </summary>
/// <remarks>
/// Sales cannot observe a Wildberries buyout: <c>supplier/orders</c> reports only active and
/// cancelled units, and the buyout appears only in the finance report. The event therefore
/// describes the line, not a fulfilment, and the Finance journal decides whether a sale happened.
/// </remarks>
public record OrderLinesChangedEvent
{
    /// <summary>
    /// Gets the changed lines of one chunk.
    /// </summary>
    public List<ChangedOrderLine> Lines { get; init; } = new();
}
