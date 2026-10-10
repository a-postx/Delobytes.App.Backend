using Delobytes.App.Backend.Integrations.Application.Interfaces;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Services;

/// <summary>
/// Ambient holder for the SyncJob currently being processed. Uses a static AsyncLocal so the
/// value flows through the logical call context regardless of which DI scope resolves a given
/// consumer — including HttpClientFactory's dedicated handler-lifetime scope, which is distinct
/// from the message consumer's own scope (see <see cref="ApiClients.RawApiResponseCaptureHandler"/>).
/// Mutation (<see cref="SetCurrent"/>/<see cref="Clear"/>) is only exposed on this concrete type;
/// consumers that must only read the value depend on <see cref="ISyncJobExecutionContext"/>.
/// </summary>
public class SyncJobExecutionContext : ISyncJobExecutionContext
{
    private static readonly AsyncLocal<Guid?> _asyncLocalSyncJobId = new AsyncLocal<Guid?>();

    /// <inheritdoc/>
    public Guid? SyncJobId => _asyncLocalSyncJobId.Value;

    /// <summary>
    /// Sets the current SyncJob id for the duration of its processing.
    /// </summary>
    /// <param name="syncJobId">SyncJob identifier.</param>
    public void SetCurrent(Guid syncJobId)
    {
        _asyncLocalSyncJobId.Value = syncJobId;
    }

    /// <summary>
    /// Clears the current SyncJob id. Must be called when processing finishes to prevent
    /// leakage into unrelated flows (e.g. connection validation calls on the same thread pool
    /// thread afterwards).
    /// </summary>
    public void Clear()
    {
        _asyncLocalSyncJobId.Value = null;
    }
}
