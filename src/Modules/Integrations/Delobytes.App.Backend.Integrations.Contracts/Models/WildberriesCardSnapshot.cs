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
    /// Additional channel-specific data serialized as JSON.
    /// </summary>
    public string? ChannelSpecificData { get; init; }
}
