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
}
