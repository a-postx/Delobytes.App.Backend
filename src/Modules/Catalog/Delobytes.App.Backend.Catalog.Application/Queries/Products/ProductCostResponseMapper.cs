using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products;

/// <summary>
/// Maps a calculated breakdown onto the API response shape.
/// Shared by the persisted-composition query and the draft preview, so both endpoints always return
/// the same field set and the client can compare them directly.
/// </summary>
public static class ProductCostResponseMapper
{
    /// <summary>
    /// Projects <paramref name="breakdown"/> into <see cref="GetProductCostResponse"/>.
    /// </summary>
    /// <param name="breakdown">Calculated cost breakdown.</param>
    /// <returns>The response the API returns for a cost calculation.</returns>
    public static GetProductCostResponse Map(CostBreakdown breakdown)
    {
        return new GetProductCostResponse
        {
            Found = true,
            ProductId = breakdown.ProductId,
            AsOfDate = breakdown.AsOfDate,
            MaterialCost = breakdown.MaterialCost,
            LogisticsCost = breakdown.LogisticsCost,
            PackagingCost = breakdown.PackagingCost,
            LaborCost = breakdown.LaborCost,
            TotalCost = breakdown.TotalCost,
            IsComplete = breakdown.IsComplete,
            Lines = breakdown.Lines
                .Select(line => new GetProductCostLineDto
                {
                    ComponentId = line.ComponentId,
                    ComponentName = line.ComponentName,
                    Category = line.Category,
                    Quantity = line.Quantity,
                    PricePerUnit = line.PricePerUnit,
                    LineTotal = line.LineTotal,
                })
                .ToList(),
            Warnings = breakdown.Warnings
                .Select(warning => new GetProductCostWarningDto
                {
                    Type = warning.Type.ToString(),
                    Message = warning.Message,
                    ComponentId = warning.ComponentId,
                })
                .ToList(),
        };
    }
}
