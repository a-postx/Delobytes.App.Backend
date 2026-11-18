using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Finance.Domain.Entities;

/// <summary>
/// Finance's projection of one Sales order line: the attribution facts the journal is matched
/// against. Mirrors <c>Sales.ChannelProductRef</c> in shape: a local copy of another module's key
/// facts, kept current by an event, so that a recompute never needs a synchronous call across the
/// module boundary.
/// </summary>
public class OrderLineRef : ITenantScoped, IRowVersionedEntity
{
    /// <summary>
    /// Gets or sets the Sales order line identifier. The primary key as well: one ref per line.
    /// </summary>
    public Guid OrderLineId { get; set; }

    /// <summary>
    /// Gets or sets the owning order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the sales channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side order identity.
    /// </summary>
    public string ExternalOrderId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the channel-side unit identity: the Wildberries <c>srid</c> or the Ozon posting
    /// number. Null when the channel reports no unit identity for the line.
    /// </summary>
    public string? ExternalLineId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side product identity: the Wildberries <c>nmId</c> or the Ozon
    /// <c>sku</c>.
    /// </summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the resolved catalogue channel product, where the link was known when the ref
    /// was written.
    /// </summary>
    public Guid? ChannelProductId { get; set; }

    /// <summary>
    /// Gets or sets the line quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the order placement instant as reported by the channel.
    /// </summary>
    public DateTimeOffset OrderDate { get; set; }

    /// <summary>
    /// Gets or sets the line currency, which becomes the currency of the P&amp;L line.
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets a value indicating whether the channel cancelled the line.
    /// </summary>
    public bool IsCancelled { get; set; }

    /// <summary>
    /// Gets or sets the time the projection was last written.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the row version for optimistic concurrency (PostgreSQL xmin).
    /// </summary>
    public uint RowVersion { get; set; }
}
