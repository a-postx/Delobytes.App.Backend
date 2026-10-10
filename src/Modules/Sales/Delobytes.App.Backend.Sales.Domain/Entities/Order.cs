using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Enums;

namespace Delobytes.App.Backend.Sales.Domain.Entities;

/// <summary>
/// Order header. Holds only facts reported by the channel — no computed money.
/// </summary>
public class Order : ITenantScoped, IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the order unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the channel identifier (Catalog.Channel.Id — no FK across modules).
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the marketplace cabinet that produced this order.
    /// </summary>
    public Guid? ConnectionId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side order identity. The upsert key together with
    /// <see cref="ChannelId"/>.
    /// </summary>
    public string ExternalOrderId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the human-facing order number, where the channel reports one that
    /// differs from <see cref="ExternalOrderId"/>.
    /// </summary>
    public string? ExternalOrderNumber { get; set; }

    /// <summary>
    /// Gets or sets the channel-reported placement time.
    /// </summary>
    public DateTimeOffset OrderDate { get; set; }

    /// <summary>
    /// Gets or sets the channel-reported time of the last status change.
    /// </summary>
    public DateTimeOffset? StatusChangedAt { get; set; }

    /// <summary>
    /// Gets or sets the canonical, channel-independent status. A lossy projection of the
    /// external representation — see <see cref="ExternalStatus"/>.
    /// </summary>
    public OrderStatus Status { get; set; } = OrderStatus.Unknown;

    /// <summary>
    /// Gets or sets the verbatim channel status string. Never normalised: the canonical
    /// <see cref="Status"/> must stay re-derivable from it.
    /// </summary>
    public string ExternalStatus { get; set; } = default!;

    /// <summary>
    /// Gets or sets the verbatim channel substatus, where the channel has one.
    /// </summary>
    public string? ExternalSubstatus { get; set; }

    /// <summary>
    /// Gets or sets the verbatim channel cancel type or reason.
    /// </summary>
    public string? CancelReason { get; set; }

    /// <summary>
    /// Gets or sets the order currency.
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets the delivery city reported by the channel.
    /// </summary>
    public string? DeliveryCity { get; set; }

    /// <summary>
    /// Gets or sets the delivery region reported by the channel.
    /// </summary>
    public string? DeliveryRegion { get; set; }

    /// <summary>
    /// Gets or sets the warehouse name reported by the channel.
    /// </summary>
    public string? WarehouseName { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the order ships from the seller's warehouse
    /// rather than the channel's own.
    /// </summary>
    public bool? IsSellerWarehouse { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the order is business-to-business.
    /// </summary>
    public bool? IsB2b { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the channel flags the order as a test order.
    /// </summary>
    public bool? IsTestOrder { get; set; }

    /// <summary>
    /// Gets or sets the order-level total, but only when the channel literally reports one.
    /// For Yandex Market this is the buyer's payment, not seller revenue.
    /// </summary>
    public decimal? ChannelReportedTotal { get; set; }

    /// <summary>
    /// Gets or sets the order-level delivery fee. Reported here (not on the line) because it is
    /// not attributed to a single line by the channel.
    /// </summary>
    public decimal? ChannelReportedDeliveryFee { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the raw API payload this version was built from.
    /// </summary>
    public Guid? RawDataId { get; set; }

    /// <summary>
    /// Gets or sets the time of the first import. Set once and never moved.
    /// </summary>
    public DateTimeOffset FirstImportedAt { get; set; }

    /// <summary>
    /// Gets or sets the time of the most recent upsert.
    /// </summary>
    public DateTimeOffset LastImportedAt { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last modification timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the row version for optimistic concurrency (PostgreSQL xmin).
    /// </summary>
    public uint RowVersion { get; set; }

    /// <summary>
    /// Gets or sets the order lines. Returns and settlements are reachable through the lines.
    /// </summary>
    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
}
