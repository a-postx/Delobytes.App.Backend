namespace Delobytes.App.Backend.Integrations.Application.Events;

/// <summary>
/// Published by the worker when a finance import run is dispatched.
/// </summary>
public record FinanceEntriesImportRequestedEvent
{
    /// <summary>Gets or sets the JobRun identifier.</summary>
    public Guid JobRunId { get; init; }

    /// <summary>Gets or sets the Connection identifier.</summary>
    public Guid ConnectionId { get; init; }
}
