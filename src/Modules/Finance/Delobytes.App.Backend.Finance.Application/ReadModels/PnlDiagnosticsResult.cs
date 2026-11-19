namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Diagnostics of a period.
/// </summary>
public sealed class PnlDiagnosticsResult
{
    public int UnattributedEntries { get; set; }

    public int AmbiguousAttributions { get; set; }

    public decimal ControlDifference { get; set; }

    public int LinesWithOpenControlDifference { get; set; }

    /// <summary>
    /// Lines in the period whose catalogue product is unknown: either Catalog has not resolved the
    /// cost yet, or the channel product is not linked to a product. Such a line is still visible in
    /// the line list and in the unlinked summary group.
    /// </summary>
    public int LinesWithoutProductLink { get; set; }

    /// <summary>Sum of <c>RevenueAmount</c> over <see cref="LinesWithoutProductLink"/>.</summary>
    public decimal UnlinkedRevenueAmount { get; set; }

    /// <summary>Lines of the period still waiting for a document inside its window.</summary>
    public int ProvisionalLines { get; set; }

    /// <summary>Lines of the period whose document is past its window.</summary>
    public int OverdueLines { get; set; }

    /// <summary>Lines of the period whose missing document was closed by a decision.</summary>
    public int ClosedWithExceptionLines { get; set; }

    /// <summary>
    /// Sum of the estimated impact of the open expectations of the period. Zero when nothing can be
    /// estimated; it is an indication of the size of the gap, not a booked amount.
    /// </summary>
    public decimal EstimatedMissingAmount { get; set; }
}
