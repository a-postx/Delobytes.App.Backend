namespace Delobytes.App.Backend.Finance.Application.ReadModels;

/// <summary>
/// Diagnostics of a period.
/// </summary>
public sealed class PnlDiagnosticsResult
{
    /// <summary>Gets or sets the currency every figure of this result is expressed in.</summary>
    public string Currency { get; set; } = default!;

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

    /// <summary>
    /// Gets or sets the number of lines of the period the reporting currency kept out. Every figure
    /// above describes <see cref="Currency"/> only, so a non-zero value means the period holds money
    /// this result does not describe.
    /// </summary>
    public int ExcludedByCurrency { get; set; }

    /// <summary>
    /// Gets or sets the excluded lines grouped by their currency, in their own currency: amounts of
    /// different currencies are never converted or added up here.
    /// </summary>
    public List<PnlCurrencyExclusion> ExcludedCurrencies { get; set; } = new();
}
