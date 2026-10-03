namespace Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

/// <summary>
/// The result of a product cost calculation for one date.
/// Costs are split by component category plus labour, so the UI can show where the money goes.
/// </summary>
/// <param name="ProductId">Product the calculation was performed for.</param>
/// <param name="AsOfDate">Date the versions of all inputs were resolved against.</param>
/// <param name="MaterialCost">Sum of lines whose component category is material.</param>
/// <param name="LogisticsCost">Sum of lines whose component category is logistics.</param>
/// <param name="PackagingCost">Sum of lines whose component category is packaging.</param>
/// <param name="LaborCost">Daily wage divided by the assembly output rate.</param>
/// <param name="TotalCost">Sum of the four cost buckets.</param>
/// <param name="Lines">BOM lines in the order returned by the repository, never re-sorted.</param>
/// <param name="Warnings">Everything that prevented a fully reliable calculation.</param>
public sealed record CostBreakdown(
    Guid ProductId,
    DateOnly AsOfDate,
    decimal MaterialCost,
    decimal LogisticsCost,
    decimal PackagingCost,
    decimal LaborCost,
    decimal TotalCost,
    IReadOnlyList<CostLine> Lines,
    IReadOnlyList<CostWarning> Warnings)
{
    /// <summary>
    /// Gets a value indicating whether every input was available on the requested date.
    /// A complete breakdown still may contain zero amounts, e.g. a component priced at zero.
    /// </summary>
    public bool IsComplete
    {
        get { return Warnings.Count == 0; }
    }
}
