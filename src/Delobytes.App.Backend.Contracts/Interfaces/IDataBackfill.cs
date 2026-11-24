namespace Delobytes.App.Backend.Contracts.Interfaces;

/// <summary>
/// One-shot data repair owned by a module and run by the explicit <c>migrate</c> step, never by
/// the web host's normal startup. Must be idempotent: the step is repeatable.
/// </summary>
public interface IDataBackfill
{
    /// <summary>
    /// Gets a short human-readable name of what the backfill repairs, for the migrate log.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Runs the repair.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public Task RunAsync(CancellationToken cancellationToken = default);
}
