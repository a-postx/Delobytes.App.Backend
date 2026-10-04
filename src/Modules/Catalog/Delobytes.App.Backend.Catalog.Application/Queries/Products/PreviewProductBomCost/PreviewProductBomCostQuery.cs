using MediatR;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.PreviewProductBomCost;

/// <summary>
/// Requests the cost breakdown of a product composition that has not been saved yet.
/// Exists so the editor can show the effect of an edit before committing it: the draft is priced
/// through the same calculator as the persisted composition, and nothing is written.
/// </summary>
public class PreviewProductBomCostQuery : IRequest<PreviewProductBomCostResponse>
{
    /// <summary>Gets or sets the product the draft composition belongs to.</summary>
    public Guid ProductId { get; set; }

    /// <summary>
    /// Gets or sets the draft composition. An empty list is a valid draft that means "no composition",
    /// which prices as a missing BOM and still reports labour.
    /// </summary>
    public List<PreviewProductBomCostLine> Lines { get; set; } = new List<PreviewProductBomCostLine>();

    /// <summary>
    /// Gets or sets the date the input versions are resolved against.
    /// Null means today, which is the only case where the current clock is consulted.
    /// </summary>
    public DateOnly? AsOf { get; set; }
}

/// <summary>
/// One draft composition position supplied by the client.
/// </summary>
public class PreviewProductBomCostLine
{
    /// <summary>Gets or sets the component identifier.</summary>
    public Guid ComponentId { get; set; }

    /// <summary>Gets or sets the component quantity per one unit of the product.</summary>
    public decimal Quantity { get; set; }
}
