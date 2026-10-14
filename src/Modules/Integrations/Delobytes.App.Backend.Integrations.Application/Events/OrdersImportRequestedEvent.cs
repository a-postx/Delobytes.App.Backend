namespace Delobytes.App.Backend.Integrations.Application.Events;

/// <summary>
/// Published by Integrations when an orders import job is initiated by the scheduler (Stage 11).
/// Consumed by Integrations.ProcessOrdersImportConsumer (Stage 9) to orchestrate the delta/backfill
/// fetch cycle. Internal to the Integrations module (not a cross-module contract), so it lives in
/// Application rather than Contracts, matching ProductsImportRequestedEvent.
/// </summary>
public record OrdersImportRequestedEvent
{
    /// <summary>
    /// Identifier of the JobRun that tracks this import.
    /// </summary>
    public Guid JobRunId { get; init; }

    /// <summary>
    /// Identifier of the Connection from which orders are imported.
    /// </summary>
    public Guid ConnectionId { get; init; }
}
