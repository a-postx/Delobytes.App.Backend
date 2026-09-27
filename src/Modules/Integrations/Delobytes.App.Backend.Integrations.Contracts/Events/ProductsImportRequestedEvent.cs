namespace Delobytes.App.Backend.Integrations.Contracts.Events;

/// <summary>
/// Published by Integrations when a products import job is initiated.
/// Consumed by Integrations.ProcessProductsImportConsumer to orchestrate cursor-based pagination.
/// </summary>
public record ProductsImportRequestedEvent
{
    /// <summary>
    /// Identifier of the SyncJob that tracks this import.
    /// </summary>
    public Guid SyncJobId { get; init; }

    /// <summary>
    /// Identifier of the Connection from which products are imported.
    /// </summary>
    public Guid ConnectionId { get; init; }
}
