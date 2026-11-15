namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// A barcode a sales channel reports for one of its cards, as exposed through the API.
/// Deliberately separate from <see cref="ProductBarcodeDto"/>: a channel copy has no catalogue
/// identity of its own, so the id and the default flag of the canonical set make no sense here.
/// </summary>
public class ChannelProductBarcodeDto
{
    /// <summary>Gets or sets the value exactly as the channel reports it.</summary>
    public string Value { get; set; } = default!;

    /// <summary>Gets or sets the barcode system the channel reports.</summary>
    public string? Type { get; set; }
}
