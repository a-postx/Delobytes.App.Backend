namespace Delobytes.App.Backend.Finance.Domain.Enums;

/// <summary>
/// Whether Catalog could resolve a product for an order line and calculate its cost.
/// </summary>
public enum OrderLineCostStatus
{
    /// <summary>
    /// A product was resolved and its cost calculated.
    /// </summary>
    Resolved = 0,

    /// <summary>
    /// The line has no catalogue link yet, so no cost can be calculated. The result is still stored:
    /// it replaces an older resolved cost because it is the newer observation.
    /// </summary>
    ProductNotLinked = 1,
}
