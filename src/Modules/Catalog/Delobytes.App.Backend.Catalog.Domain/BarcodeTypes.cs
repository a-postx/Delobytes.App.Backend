namespace Delobytes.App.Backend.Catalog.Domain;

/// <summary>
/// Known barcode symbologies used by <see cref="Entities.ProductBarcode.Type"/>.
/// Free text on purpose: a further symbology reaches the same field without a schema change.
/// </summary>
public static class BarcodeTypes
{
    /// <summary>EAN-13, the symbology printed on a retail product label.</summary>
    public const string Ean13 = "EAN13";
}
