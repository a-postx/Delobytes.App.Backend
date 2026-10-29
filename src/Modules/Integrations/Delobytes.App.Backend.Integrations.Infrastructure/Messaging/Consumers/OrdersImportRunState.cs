namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers;

/// <summary>
/// Mutable counters of one orders import run. Created per ProcessAsync call; never stored on a
/// scoped service.
/// </summary>
internal sealed class OrdersImportRunState
{
    public OrdersImportRunState(DateTimeOffset deadline)
    {
        Deadline = deadline;
        StopReason = "Completed";
    }

    public DateTimeOffset Deadline { get; }

    public int RequestsIssued { get; set; }

    public int InProcessWaits { get; set; }

    public TimeSpan TotalWaited { get; set; }

    public string StopReason { get; set; }

    public Guid? ContinuationId { get; set; }

    public DateTimeOffset? ContinuationNotBefore { get; set; }

    public DateTimeOffset LastRequestStartedAt { get; set; }

    public int RowsWithoutIdentity { get; set; }
}
