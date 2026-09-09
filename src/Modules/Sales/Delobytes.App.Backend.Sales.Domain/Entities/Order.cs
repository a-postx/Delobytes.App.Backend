using Delobytes.App.Backend.Contracts.Interfaces;
using Delobytes.App.Backend.Sales.Domain.Enums;

namespace Delobytes.App.Backend.Sales.Domain.Entities;

/// <summary>
/// Represents an order from a sales channel.
/// </summary>
public class Order : ITenantScoped
{
    /// <summary>
    /// Gets or sets the order unique identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the channel product identifier.
    /// </summary>
    public Guid ChannelProductId { get; set; }

    /// <summary>
    /// Gets or sets the external order identifier in the marketplace system.
    /// </summary>
    public string ExternalOrderId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the channel identifier.
    /// </summary>
    public Guid ChannelId { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the order was placed.
    /// </summary>
    public DateTimeOffset OrderDate { get; set; }

    /// <summary>
    /// Gets or sets the quantity of items ordered.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the total revenue from the order.
    /// </summary>
    public decimal Revenue { get; set; }

    /// <summary>
    /// Gets or sets the commission charged by the marketplace.
    /// </summary>
    public decimal Commission { get; set; }

    /// <summary>
    /// Gets or sets the net revenue (Revenue - Commission).
    /// </summary>
    public decimal NetRevenue { get; set; }

    /// <summary>
    /// Gets or sets the order status.
    /// </summary>
    public OrderStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the raw API response identifier (nullable).
    /// </summary>
    public Guid? RawDataId { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the order was imported.
    /// </summary>
    public DateTimeOffset ImportedAt { get; set; }

    /// <summary>
    /// Gets or sets the date and time when the order was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Navigation property: returns associated with this order.
    /// </summary>
    public ICollection<Return> Returns { get; set; } = new List<Return>();
}
