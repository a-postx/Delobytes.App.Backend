namespace Delobytes.App.Backend.Catalog.Domain.Enums;

/// <summary>
/// Tax regime applied to the tenant's revenue.
/// </summary>
public enum TaxType
{
    /// <summary>Simplified tax system (УСН).</summary>
    Usn = 0,

    /// <summary>Value-added tax (НДС).</summary>
    Vat = 1
}
