namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Read-only ambient accessor for the JobRun currently being processed by a message consumer.
/// Flows across async calls (including HttpClient message handlers resolved from a separate
/// handler-lifetime DI scope) so API clients can attribute a captured raw response to the right
/// JobRun without threading the id through every method signature.
/// </summary>
public interface IJobRunExecutionContext
{
    /// <summary>
    /// Gets the identifier of the JobRun being processed in the current logical call chain,
    /// or null outside of JobRun processing (e.g. connection validation calls).
    /// </summary>
    public Guid? JobRunId { get; }

    /// <summary>
    /// Gets the connection the current run imports through, so a captured raw response can be
    /// attributed to a marketplace cabinet without a lookup of its own. Null outside of JobRun
    /// processing.
    /// </summary>
    public Guid? ConnectionId { get; }

    /// <summary>
    /// Gets the channel of the connection the current run imports through. Null outside of JobRun
    /// processing.
    /// </summary>
    public Guid? ChannelId { get; }
}
