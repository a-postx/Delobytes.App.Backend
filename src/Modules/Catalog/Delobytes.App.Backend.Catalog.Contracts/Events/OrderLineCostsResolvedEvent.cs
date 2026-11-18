namespace Delobytes.App.Backend.Catalog.Contracts.Events;

/// <summary>
/// Published by Catalog after resolving the production cost of the order lines it was told about.
/// Consumed by Finance to store the cost fact and recompute the P&amp;L line.
/// </summary>
public record OrderLineCostsResolvedEvent
{
    /// <summary>Gets the resolved costs of one message's lines.</summary>
    public List<OrderLineCostResult> Results { get; init; } = new List<OrderLineCostResult>();
}
