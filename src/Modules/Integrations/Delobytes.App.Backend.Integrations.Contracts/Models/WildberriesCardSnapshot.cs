namespace Delobytes.App.Backend.Integrations.Contracts.Models;

/// <summary>
/// Normalized snapshot of a Wildberries product card.
/// Contract model without dependencies on EF Core or HTTP client types.
/// </summary>
public record WildberriesCardSnapshot
{
    /// <summary>
    /// Wildberries internal product identifier (nmID).
    /// </summary>
    public long NmId { get; init; }

    /// <summary>
    /// Product name.
    /// </summary>
    public string Name { get; init; } = default!;

    /// <summary>
    /// Vendor code (артикул поставщика).
    /// </summary>
    public string VendorCode { get; init; } = default!;

    /// <summary>
    /// List of barcodes associated with the product.
    /// </summary>
    public List<string> Barcodes { get; init; } = new();

    /// <summary>
    /// Product description.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Outer package length in cm, as reported by the marketplace.
    /// </summary>
    public decimal? LengthCm { get; init; }

    /// <summary>
    /// Outer package width in cm, as reported by the marketplace.
    /// </summary>
    public decimal? WidthCm { get; init; }

    /// <summary>
    /// Outer package height in cm, as reported by the marketplace.
    /// </summary>
    public decimal? HeightCm { get; init; }

    /// <summary>
    /// Gross weight of the packed unit in kg, as reported by the marketplace.
    /// </summary>
    public decimal? WeightKg { get; init; }

    /// <summary>
    /// Additional channel-specific data serialized as JSON.
    /// </summary>
    public string? ChannelSpecificData { get; init; }
}
