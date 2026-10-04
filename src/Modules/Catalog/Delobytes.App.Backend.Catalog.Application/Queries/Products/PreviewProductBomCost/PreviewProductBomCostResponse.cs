using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;
using Delobytes.App.Backend.Catalog.Application.Services.CostCalculation;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.PreviewProductBomCost;

/// <summary>
/// Cost breakdown of an unsaved composition.
/// Shares the shape of <see cref="GetProductCostResponse"/> so the client can diff the two without
/// special-casing this endpoint.
/// </summary>
public class PreviewProductBomCostResponse
{
    /// <summary>Gets or sets a value indicating whether the product exists.</summary>
    public bool Found { get; set; }

    /// <summary>Gets or sets the cost breakdown of the draft.</summary>
    public GetProductCostResponse? Preview { get; set; }
}
