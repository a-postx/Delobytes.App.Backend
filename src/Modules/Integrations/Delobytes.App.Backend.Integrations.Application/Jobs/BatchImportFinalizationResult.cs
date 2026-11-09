namespace Delobytes.App.Backend.Integrations.Application.Jobs;

/// <summary>
/// Outcome of an attempt to finalise a batch-driven import run.
/// </summary>
public enum BatchImportFinalizationResult
{
    /// <summary>
    /// Not every expected batch result is recorded yet, or the expected count is not known, so the
    /// run must stay open.
    /// </summary>
    NotReady,

    /// <summary>
    /// The run is ready to finalise but its checkpoint payload is missing or unreadable, so the
    /// watermark cannot be resolved.
    /// </summary>
    CheckpointUnavailable,

    /// <summary>
    /// This call wrote the final counters and status. The caller still owes downstream consumers the
    /// completion event.
    /// </summary>
    Finalized,

    /// <summary>
    /// Another writer finalised the run first. The losing writer steps aside silently: the winner
    /// has already published the completion event.
    /// </summary>
    AlreadyFinalized,
}
