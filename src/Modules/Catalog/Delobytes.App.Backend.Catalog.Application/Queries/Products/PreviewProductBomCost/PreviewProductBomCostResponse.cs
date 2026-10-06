using Delobytes.App.Backend.Catalog.Application.Queries.Products.GetProductCost;

namespace Delobytes.App.Backend.Catalog.Application.Queries.Products.PreviewProductBomCost;

/// <summary>
/// Cost breakdown of an unsaved composition, together with the persisted composition it was compared against.
/// Both sides are produced from a single read of the database, so they cannot drift apart the way two
/// independent requests would when the composition is edited in parallel.
/// </summary>
public class PreviewProductBomCostResponse
{
    /// <summary>Gets or sets a value indicating whether the product exists.</summary>
    public bool Found { get; set; }

    /// <summary>Gets or sets the cost breakdown of the draft.</summary>
    public GetProductCostResponse? Preview { get; set; }

    /// <summary>Gets or sets the summary of the persisted composition on the same date.</summary>
    public PreviewProductBomCostBaseline? Baseline { get; set; }

    /// <summary>Gets or sets the difference between the draft and the persisted composition.</summary>
    public PreviewProductBomCostDelta? Delta { get; set; }
}

/// <summary>
/// Summary of the persisted composition, reduced to the buckets the editor shows.
/// Deliberately not <see cref="GetProductCostResponse"/>: the baseline is only ever compared against,
/// never displayed line by line, so shipping its BOM lines would duplicate the whole composition on
/// every debounced keystroke for nothing.
/// </summary>
public class PreviewProductBomCostBaseline
{
    /// <summary>Gets or sets the cost of material components.</summary>
    public decimal MaterialCost { get; set; }

    /// <summary>Gets or sets the cost of logistics components.</summary>
    public decimal LogisticsCost { get; set; }

    /// <summary>Gets or sets the cost of packaging components.</summary>
    public decimal PackagingCost { get; set; }

    /// <summary>Gets or sets the assembly labour cost.</summary>
    public decimal LaborCost { get; set; }

    /// <summary>Gets or sets the total cost of one unit.</summary>
    public decimal TotalCost { get; set; }

    /// <summary>Gets or sets a value indicating whether every input was available on the requested date.</summary>
    public bool IsComplete { get; set; }
}

/// <summary>
/// Signed difference between a draft composition and the persisted one on the same date.
/// Labour is included so the client does not have to infer why it never moves.
/// </summary>
public class PreviewProductBomCostDelta
{
    /// <summary>Gets or sets the material cost change.</summary>
    public decimal MaterialDelta { get; set; }

    /// <summary>Gets or sets the logistics cost change.</summary>
    public decimal LogisticsDelta { get; set; }

    /// <summary>Gets or sets the packaging cost change.</summary>
    public decimal PackagingDelta { get; set; }

    /// <summary>Gets or sets the labour cost change, which stays zero while the output rate is unchanged.</summary>
    public decimal LaborDelta { get; set; }

    /// <summary>Gets or sets the total cost change.</summary>
    public decimal TotalDelta { get; set; }
}
