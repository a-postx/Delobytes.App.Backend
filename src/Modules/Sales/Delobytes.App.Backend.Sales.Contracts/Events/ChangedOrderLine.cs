namespace Delobytes.App.Backend.Sales.Contracts.Events;

/// <summary>
/// One order line whose attribution facts are published to Finance. Carries no money: Finance reads
/// the money from the channel reports and uses these facts only to bind an entry to a sale.
/// </summary>
public record ChangedOrderLine
{
    /// <summary>
    /// Gets the order line identifier.
    /// </summary>
    public Guid OrderLineId { get; init; }

    /// <summary>
    /// Gets the owning order identifier.
    /// </summary>
    public Guid OrderId { get; init; }

    /// <summary>
    /// Gets the sales channel identifier.
    /// </summary>
    public Guid ChannelId { get; init; }

    /// <summary>
    /// Gets the channel-side order identity.
    /// </summary>
    public string ExternalOrderId { get; init; } = default!;

    /// <summary>
    /// Gets the channel-side unit identity: the Wildberries <c>srid</c> or the Ozon posting number.
    /// </summary>
    public string? ExternalLineId { get; init; }

    /// <summary>
    /// Gets the channel-side product identity.
    /// </summary>
    public string ExternalProductId { get; init; } = default!;

    /// <summary>
    /// Gets the resolved catalogue channel product, where known.
    /// </summary>
    public Guid? ChannelProductId { get; init; }

    /// <summary>
    /// Gets the line quantity.
    /// </summary>
    public int Quantity { get; init; }

    /// <summary>
    /// Gets the order placement instant.
    /// </summary>
    public DateTimeOffset OrderDate { get; init; }

    /// <summary>
    /// Gets the line currency.
    /// </summary>
    public string Currency { get; init; } = default!;

    /// <summary>
    /// Gets a value indicating whether the channel cancelled the line.
    /// </summary>
    public bool IsCancelled { get; init; }

    /// <summary>
    /// Gets a value indicating whether the line row was added by this import, so the consumer can
    /// tell a first registration from a correction.
    /// </summary>
    public bool IsNew { get; init; }

    /// <summary>
    /// Gets a value indicating whether the facts that decide the production cost changed.
    /// </summary>
    public bool CostRelevantChange { get; init; }
}
