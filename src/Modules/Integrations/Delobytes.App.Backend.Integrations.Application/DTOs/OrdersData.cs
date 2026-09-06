namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// Represents orders data retrieved from a marketplace channel.
/// </summary>
public class OrdersData
{
    /// <summary>
    /// Gets or sets the collection of orders.
    /// </summary>
    public ICollection<OrderItem> Orders { get; set; } = new List<OrderItem>();

    /// <summary>
    /// Gets or sets the total count of orders.
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Represents a single order item.
/// </summary>
public class OrderItem
{
    /// <summary>
    /// Gets or sets the order identifier from the channel.
    /// </summary>
    public string ExternalOrderId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the order date.
    /// </summary>
    public DateTimeOffset OrderDate { get; set; }

    /// <summary>
    /// Gets or sets the order status.
    /// </summary>
    public string Status { get; set; } = default!;

    /// <summary>
    /// Gets or sets the total amount.
    /// </summary>
    public decimal TotalAmount { get; set; }

    /// <summary>
    /// Gets or sets the currency code.
    /// </summary>
    public string Currency { get; set; } = default!;

    /// <summary>
    /// Gets or sets additional order data as JSON.
    /// </summary>
    public string? RawData { get; set; }
}
