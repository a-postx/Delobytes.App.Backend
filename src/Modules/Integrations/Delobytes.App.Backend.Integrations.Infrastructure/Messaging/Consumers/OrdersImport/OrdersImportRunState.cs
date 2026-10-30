namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Mutable counters of one orders import run. Created per ProcessAsync call; never stored on a
/// scoped service.
/// </summary>
internal sealed class OrdersImportRunState
{
    public OrdersImportRunState(DateTimeOffset deadline)
    {
        Deadline = deadline;
        StopReason = StopReason.Completed;
    }

    public DateTimeOffset Deadline { get; }

    public int RequestsIssued { get; set; }

    public int InProcessWaits { get; set; }

    public TimeSpan TotalWaited { get; set; }

    public StopReason StopReason { get; set; }

    /// <summary>
    /// Gets or sets the instant the source will accept the next request, set only when the run
    /// stopped because of a throttle or the run budget.
    /// </summary>
    public DateTimeOffset? ResumeAt { get; set; }

    public Guid? ContinuationId { get; set; }

    public DateTimeOffset? ContinuationNotBefore { get; set; }

    public DateTimeOffset LastRequestStartedAt { get; set; }

    public int RowsWithoutIdentity { get; set; }
}
