namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Read-only ambient accessor for the SyncJob currently being processed by a message consumer.
/// Flows across async calls (including HttpClient message handlers resolved from a separate
/// handler-lifetime DI scope) so API clients can attribute a captured raw response to the right
/// SyncJob without threading the id through every method signature.
/// </summary>
public interface ISyncJobExecutionContext
{
    /// <summary>
    /// Gets the identifier of the SyncJob being processed in the current logical call chain,
    /// or null outside of SyncJob processing (e.g. connection validation calls).
    /// </summary>
    Guid? SyncJobId { get; }
}
