namespace Delobytes.App.Backend.Sales.Application.Options;

/// <summary>
/// Tuning of the order line change publication.
/// </summary>
public sealed class SalesImportOptions
{
    /// <summary>
    /// Gets or sets the maximum number of lines in one <c>OrderLinesChangedEvent</c>. A message must
    /// stay small: production runs with 1 GB of memory and two concurrent consumer slots.
    /// </summary>
    public int OrderLinesChangedChunkSize { get; set; } = 500;
}
