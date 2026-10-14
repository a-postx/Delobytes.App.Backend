namespace Delobytes.App.Backend.Integrations.Domain.Entities;

/// <summary>
/// Type-specific data for a JobRun of type ProductsImport. Keyed by the owning JobRun's Id.
/// </summary>
public class ProductsImportJob
{
    /// <summary>
    /// Gets or sets the identifier, shared with and referencing the owning <see cref="JobRun"/>.
    /// </summary>
    public Guid JobRunId { get; set; }

    /// <summary>
    /// Gets or sets the total number of import batches expected for this run, known only once
    /// the terminal batch (ProductBatchImportRequestedEvent.IsLastBatch) has been published.
    /// Null until then; used by the aggregator to decide whether every batch has been recorded.
    /// </summary>
    public int? TotalImportBatches { get; set; }

    /// <summary>
    /// Navigation property: the owning job run.
    /// </summary>
    public JobRun JobRun { get; set; } = default!;
}
