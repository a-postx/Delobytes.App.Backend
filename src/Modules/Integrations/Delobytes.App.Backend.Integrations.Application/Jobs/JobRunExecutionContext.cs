using Delobytes.App.Backend.Integrations.Application.Interfaces;

namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// Ambient holder for the JobRun currently being processed. Uses a static AsyncLocal so the
/// value flows through the logical call context regardless of which DI scope resolves a given
/// consumer — including HttpClientFactory's dedicated handler-lifetime scope, which is distinct
/// from the message consumer's own scope (see RawApiResponseCaptureHandler in the infrastructure
/// layer). Mutation (<see cref="SetCurrent"/>/<see cref="Clear"/>) is only exposed on this
/// concrete type; consumers that must only read the value depend on
/// <see cref="IJobRunExecutionContext"/>, and orchestration opens a run scope through
/// <see cref="IJobRunExecutionScope"/>.
/// </summary>
public class JobRunExecutionContext : IJobRunExecutionContext, IJobRunExecutionScope
{
    private static readonly AsyncLocal<AmbientRun?> _ambientRun = new AsyncLocal<AmbientRun?>();

    /// <inheritdoc/>
    public Guid? JobRunId => _ambientRun.Value?.JobRunId;

    /// <inheritdoc/>
    public Guid? ConnectionId => _ambientRun.Value?.ConnectionId;

    /// <inheritdoc/>
    public Guid? ChannelId => _ambientRun.Value?.ChannelId;

    /// <summary>
    /// Sets the current run for the duration of its processing.
    /// </summary>
    /// <param name="jobRunId">JobRun identifier.</param>
    /// <param name="connectionId">Connection the run imports through, where known.</param>
    /// <param name="channelId">Channel of that connection, where known.</param>
    public void SetCurrent(Guid jobRunId, Guid? connectionId = null, Guid? channelId = null)
    {
        _ambientRun.Value = new AmbientRun(jobRunId, connectionId, channelId);
    }

    /// <summary>
    /// Clears the current run. Must be called when processing finishes to prevent
    /// leakage into unrelated flows (e.g. connection validation calls on the same thread pool
    /// thread afterwards).
    /// </summary>
    public void Clear()
    {
        _ambientRun.Value = null;
    }

    /// <summary>
    /// Sets the current run for the lifetime of the returned scope and restores the previous
    /// value on dispose — the previous value, not null, so a nested scope cannot clobber an outer run.
    /// </summary>
    /// <param name="jobRunId">JobRun identifier.</param>
    /// <param name="connectionId">Connection the run imports through, where known.</param>
    /// <param name="channelId">Channel of that connection, where known.</param>
    public IDisposable EnterScope(Guid jobRunId, Guid? connectionId = null, Guid? channelId = null)
    {
        AmbientRun? previous = _ambientRun.Value;
        _ambientRun.Value = new AmbientRun(jobRunId, connectionId, channelId);

        return new Scope(previous);
    }

    /// <summary>
    /// What a captured raw response is attributed to: the run itself, plus the cabinet and channel
    /// it went through. The last two are optional because they are not needed to identify a run.
    /// </summary>
    private sealed record AmbientRun(Guid JobRunId, Guid? ConnectionId, Guid? ChannelId);

    private sealed class Scope : IDisposable
    {
        private readonly AmbientRun? _previous;
        private bool _disposed;

        public Scope(AmbientRun? previous)
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
            _ambientRun.Value = _previous;
        }
    }
}
