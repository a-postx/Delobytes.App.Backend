namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Signals that an orders fetch stopped before completing the window: either the run's deadline
/// or request budget was reached (<c>Reason == "Budget"</c>), or the source throttled a request
/// beyond what the run can wait out in-process (<c>Reason == "Throttled"</c>).
/// </summary>
internal sealed class OrdersFetchStoppedException : Exception
{
    public OrdersFetchStoppedException(string reason, DateTimeOffset resumeAt)
    {
        Reason = reason;
        ResumeAt = resumeAt;
    }

    public string Reason { get; }

    public DateTimeOffset ResumeAt { get; }
}
