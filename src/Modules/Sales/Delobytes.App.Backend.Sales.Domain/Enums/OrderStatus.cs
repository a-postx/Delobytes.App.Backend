namespace Delobytes.App.Backend.Sales.Domain.Enums;

/// <summary>
/// Canonical, channel-independent order lifecycle. A lossy projection: the verbatim channel status
/// stays on the entity, so this value must remain re-derivable from it.
/// Money receipt is not a status — it is a settlement concern.
/// </summary>
public enum OrderStatus
{
    /// <summary>
    /// The channel status is not recognised by the projection.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// Placed but not yet confirmed by the channel.
    /// </summary>
    Created = 1,

    /// <summary>
    /// Confirmed and being assembled.
    /// </summary>
    Processing = 2,

    /// <summary>
    /// Handed over to delivery.
    /// </summary>
    Shipped = 3,

    /// <summary>
    /// Received by the buyer or bought out — terminal success.
    /// </summary>
    Delivered = 4,

    /// <summary>
    /// Cancelled — terminal failure.
    /// </summary>
    Cancelled = 5,

    /// <summary>
    /// The goods came back.
    /// </summary>
    Returned = 6,

    /// <summary>
    /// Only part of the order came back.
    /// </summary>
    PartiallyReturned = 7,

    /// <summary>
    /// A dispute or arbitration is in progress.
    /// </summary>
    Unresolved = 8,
}
