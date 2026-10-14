namespace Delobytes.App.Backend.Integrations.Application.Events;

/// <summary>
/// Published by Integrations when a products import job is initiated.
/// Consumed by Integrations.ProcessProductsImportConsumer to orchestrate cursor-based pagination.
/// Internal to the Integrations module (not a cross-module contract), so it lives in
/// Application rather than Contracts.
/// </summary>
public record ProductsImportRequestedEvent
{
    /// <summary>
    /// Identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Identifier of the Connection from which products are imported.
    /// </summary>
    public Guid ConnectionId { get; init; }
}
