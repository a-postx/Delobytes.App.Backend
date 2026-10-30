using Delobytes.App.Backend.Integrations.Application.Interfaces;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Services;

/// <summary>
/// Ambient holder for the JobRun currently being processed. Uses a static AsyncLocal so the
/// value flows through the logical call context regardless of which DI scope resolves a given
/// consumer — including HttpClientFactory's dedicated handler-lifetime scope, which is distinct
/// from the message consumer's own scope (see <see cref="ApiClients.RawApiResponseCaptureHandler"/>).
/// Mutation (<see cref="SetCurrent"/>/<see cref="Clear"/>) is only exposed on this concrete type;
/// consumers that must only read the value depend on <see cref="IJobRunExecutionContext"/>.
/// </summary>
public class JobRunExecutionContext : IJobRunExecutionContext
{
    private static readonly AsyncLocal<Guid?> _asyncLocalJobRunId = new AsyncLocal<Guid?>();

    /// <inheritdoc/>
    public Guid? JobRunId => _asyncLocalJobRunId.Value;

    /// <summary>
    /// Sets the current JobRun id for the duration of its processing.
    /// </summary>
    /// <param name="jobRunId">JobRun identifier.</param>
    public void SetCurrent(Guid jobRunId)
    {
        _asyncLocalJobRunId.Value = jobRunId;
    }

    /// <summary>
    /// Clears the current JobRun id. Must be called when processing finishes to prevent
    /// leakage into unrelated flows (e.g. connection validation calls on the same thread pool
    /// thread afterwards).
    /// </summary>
    public void Clear()
    {
        _asyncLocalJobRunId.Value = null;
    }

    /// <summary>
    /// Sets the current JobRun id for the lifetime of the returned scope and restores the previous
    /// value on dispose — the previous value, not null, so a nested scope cannot clobber an outer run.
    /// </summary>
    /// <param name="jobRunId">JobRun identifier.</param>
    public IDisposable EnterScope(Guid jobRunId)
    {
        Guid? previous = _asyncLocalJobRunId.Value;
        _asyncLocalJobRunId.Value = jobRunId;

        return new Scope(previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Guid? _previous;
        private bool _disposed;

        public Scope(Guid? previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _asyncLocalJobRunId.Value = _previous;
        }
    }
}
