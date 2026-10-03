namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

/// <summary>
/// Reason a cost breakdown is not complete.
/// </summary>
public enum CostWarningType
{
    /// <summary>No component price was effective on the requested date.</summary>
    MissingComponentPrice,

    /// <summary>No work rate or assembly output rate was effective on the requested date.</summary>
    MissingWorkRate,

    /// <summary>The product has no BOM version effective on the requested date.</summary>
    MissingBom,

    /// <summary>A quantity or rate is zero or negative, so the affected part of the cost is not computed.</summary>
    InvalidQuantity,
}
