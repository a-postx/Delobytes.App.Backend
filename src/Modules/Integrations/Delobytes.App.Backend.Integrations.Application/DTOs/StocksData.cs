namespace Delobytes.App.Backend.Integrations.Application.DTOs;

/// <summary>
/// Represents stock data retrieved from a marketplace channel.
/// </summary>
public class StocksData
{
    /// <summary>
    /// Gets or sets the collection of stock items.
    /// </summary>
    public ICollection<StockItem> Stocks { get; set; } = new List<StockItem>();

    /// <summary>
    /// Gets or sets the total count of stock items.
    /// </summary>
    public int TotalCount { get; set; }
}

/// <summary>
/// Represents a single stock item.
/// </summary>
public class StockItem
{
    /// <summary>
    /// Gets or sets the SKU (Stock Keeping Unit).
    /// </summary>
    public string Sku { get; set; } = default!;

    /// <summary>
    /// Gets or sets the product identifier from the channel.
    /// </summary>
    public string ExternalProductId { get; set; } = default!;

    /// <summary>
    /// Gets or sets the warehouse identifier.
    /// </summary>
    public string? WarehouseId { get; set; }

    /// <summary>
    /// Gets or sets the available quantity.
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// Gets or sets the reserved quantity.
    /// </summary>
    public int ReservedQuantity { get; set; }

    /// <summary>
    /// Gets or sets the date when the stock data was last updated.
    /// </summary>
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Gets or sets additional stock data as JSON.
    /// </summary>
    public string? RawData { get; set; }
}
