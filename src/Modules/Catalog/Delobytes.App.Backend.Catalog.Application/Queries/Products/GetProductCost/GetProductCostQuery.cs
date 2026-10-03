using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;

/// <summary>
/// Requests the cost breakdown of one product.
/// </summary>
public class GetProductCostQuery : IRequest<GetProductCostResponse>
{
    /// <summary>Gets or sets the product to calculate.</summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the date the input versions are resolved against.
    /// Null means today, which is the only case where the current clock is consulted.
    /// </summary>
    public DateOnly? AsOf { get; set; }
}
