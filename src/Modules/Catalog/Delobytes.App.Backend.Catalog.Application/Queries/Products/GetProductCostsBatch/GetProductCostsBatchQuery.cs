using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCostsBatch;

/// <summary>
/// Requests the cost breakdown of multiple products in a single round-trip.
/// </summary>
public class GetProductCostsBatchQuery : IRequest<GetProductCostsBatchResponse>
{
    /// <summary>Gets or sets the list of products to calculate (max 100).</summary>
    public List<Guid> ProductIds { get; set; } = new List<Guid>();

    /// <summary>
    /// Gets or sets the date the input versions are resolved against.
    /// Null means today, which is the only case where the current clock is consulted.
    /// </summary>
    public DateOnly? AsOf { get; set; }
}
