namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Normalized snapshot of one order as the channel reported it. Carries reported facts only:
/// nothing here is derived, and nothing may be zero-substituted for a missing value.
/// Contract model without dependencies on EF Core or HTTP client types.
/// </summary>
public record ChannelOrderSnapshot
{
    /// <summary>
    /// Gets the channel's order identity.
    /// </summary>
    public string ExternalOrderId { get; init; } = default!;

    /// <summary>
    /// Gets the human-facing order number, where it differs from the identity.
    /// </summary>
    public string? ExternalOrderNumber { get; init; }

    /// <summary>
    /// Gets the channel-reported placement time.
    /// </summary>
    public DateTimeOffset OrderDate { get; init; }

    /// <summary>
    /// Gets the channel-reported time of the last status change.
    /// </summary>
    public DateTimeOffset? StatusChangedAt { get; init; }

    /// <summary>
    /// Gets the channel's status string, verbatim.
    /// </summary>
    public string ExternalStatus { get; init; } = default!;

    /// <summary>
    /// Gets the channel's substatus string, verbatim. Only where the channel has one.
    /// </summary>
    public string? ExternalSubstatus { get; init; }

    /// <summary>
    /// Gets the channel's cancel type or reason, verbatim.
    /// </summary>
    public string? CancelReason { get; init; }

    /// <summary>
    /// Gets the order currency.
    /// </summary>
    public string Currency { get; init; } = default!;

    /// <summary>
    /// Gets the delivery city.
    /// </summary>
    public string? DeliveryCity { get; init; }

    /// <summary>
    /// Gets the delivery region.
    /// </summary>
    public string? DeliveryRegion { get; init; }

    /// <summary>
    /// Gets the warehouse name the channel reported.
    /// </summary>
    public string? WarehouseName { get; init; }

    /// <summary>
    /// Gets a value indicating whether the order ships from the seller's own warehouse.
    /// </summary>
    public bool? IsSellerWarehouse { get; init; }

    /// <summary>
    /// Gets a value indicating whether the order is a business-to-business order.
    /// </summary>
    public bool? IsB2b { get; init; }

    /// <summary>
    /// Gets a value indicating whether the channel marked the order as a test order.
    /// </summary>
    public bool? IsTestOrder { get; init; }

    /// <summary>
    /// Gets the order total exactly as the channel reported it. Only set where the channel
    /// literally reports an order-level total, and it is the buyer's payment rather than
    /// seller revenue.
    /// </summary>
    public decimal? ChannelReportedTotal { get; init; }

    /// <summary>
    /// Gets the order-level delivery fee exactly as the channel reported it.
    /// </summary>
    public decimal? ChannelReportedDeliveryFee { get; init; }

    /// <summary>
    /// Gets the order lines.
    /// </summary>
    public List<ChannelOrderLineSnapshot> Lines { get; init; } = new();
}
