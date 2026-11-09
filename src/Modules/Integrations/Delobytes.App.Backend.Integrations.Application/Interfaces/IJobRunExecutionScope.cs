namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Mutating counterpart of <see cref="IJobRunExecutionContext"/>: opens an ambient scope that
/// exposes the JobRun being processed for the lifetime of that scope. Orchestration in the
/// Application layer depends on this interface, so the ambient storage itself stays an
/// infrastructure detail.
/// </summary>
public interface IJobRunExecutionScope
{
    /// <summary>
    /// Sets the current JobRun id for the lifetime of the returned scope and restores the previous
    /// value on dispose — the previous value, not null, so a nested scope cannot clobber an outer run.
    /// </summary>
    /// <param name="jobRunId">JobRun identifier.</param>
    /// <returns>Scope that restores the previous value when disposed.</returns>
    public IDisposable EnterScope(Guid jobRunId);
}
