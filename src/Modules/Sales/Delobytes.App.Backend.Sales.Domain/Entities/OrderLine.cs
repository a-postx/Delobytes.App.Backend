using Delobytes.App.Backend.Contracts.Interfaces;

namespace Delobytes.App.Backend.Sales.Domain.Entities;

/// <summary>
/// A single position of an order. Holds only facts reported by the channel.
/// </summary>
public class OrderLine : ITenantScoped
{
    /// <summary>
    /// Gets or sets the order line unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the owning order identifier.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the stable, deterministic line identity derived by the channel mapper.
    /// The upsert matches on it, so it must be reproducible across refetches of the same order.
    /// </summary>
    public string LineKey { get; set; } = default!;

    /// <summary>
    /// Gets or sets the channel-side line identity, where the channel exposes one.
    /// </summary>
    public string? ExternalLineId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side product identity (WB nmId, Ozon sku, YM shopSku, ...).
    /// Retained even after the catalogue link is resolved, so the link can be backfilled.
    /// </summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the seller's own SKU as reported by the channel.
    /// </summary>
    public string? OfferId { get; set; }

    /// <summary>
    /// Gets or sets the channel-side size identity (WB chrtId and equivalents).
    /// </summary>
    public string? ExternalSizeId { get; set; }

    /// <summary>
    /// Gets or sets the product name as reported at order time — a verbatim snapshot, not a
    /// live join to the catalogue.
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// Gets or sets the VAT code as reported by the channel.
    /// </summary>
    public string? Vat { get; set; }

    /// <summary>
    /// Gets or sets the quantity of units.
    /// </summary>
    public int Quantity { get; set; } = 1;

    /// <summary>
    /// Gets or sets the seller's unit price after the seller's own discount.
    /// </summary>
    public decimal? UnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the unit price paid by the buyer, where the channel reports it separately.
    /// </summary>
    public decimal? BuyerUnitPrice { get; set; }

    /// <summary>
    /// Gets or sets the unit price before discount, where the channel reports it.
    /// </summary>
    public decimal? UnitPriceBeforeDiscount { get; set; }

    /// <summary>
    /// Gets or sets the line currency.
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets the resolved link to the catalogue channel product. Nullable by design:
    /// orders can arrive before the catalogue import created the matching entry.
    /// </summary>
    public Guid? ChannelProductId { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the last modification timestamp.
    /// </summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the owning order.
    /// </summary>
    public Order Order { get; set; } = default!;

    /// <summary>
    /// Gets or sets the resolved money for this line.
    /// </summary>
    public ICollection<OrderSettlement> Settlements { get; set; } = new List<OrderSettlement>();

    /// <summary>
    /// Gets or sets the returns registered for this line.
    /// </summary>
    public ICollection<Return> Returns { get; set; } = new List<Return>();
}
