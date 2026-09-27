namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations.ProcessProductsImportConsumer when the entire products import job completes.
/// Consumed by Integrations to update the SyncJob final status.
/// </summary>
public record ProductsImportCompletedEvent
{
    /// <summary>
    /// Identifier of the SyncJob that tracks this import.
    /// </summary>
    public Guid SyncJobId { get; init; }

    /// <summary>
    /// True if the import completed successfully.
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// Error message if the import failed or partially succeeded.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Total number of product cards processed across all batches.
    /// </summary>
    public int TotalRecordsProcessed { get; init; }

    /// <summary>
    /// Total number of products created.
    /// </summary>
    public int TotalRecordsCreated { get; init; }

    /// <summary>
    /// Total number of products updated.
    /// </summary>
    public int TotalRecordsUpdated { get; init; }

    /// <summary>
    /// Total number of products skipped.
    /// </summary>
    public int TotalRecordsSkipped { get; init; }

    /// <summary>
    /// Total number of products that failed to import.
    /// </summary>
    public int TotalRecordsFailed { get; init; }
}
