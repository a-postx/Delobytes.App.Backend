namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Mutable progress for one orders import run: batch numbering, request budget, and pacing
/// accounting. The same instance is shared with all fetch helpers so each contributor sees the
/// counters advanced by its peers.
/// </summary>
public sealed class OrdersRunProgress
{
    public OrdersRunProgress(int nextBatchIndex)
    {
        NextBatchIndex = nextBatchIndex;
    }

    public int NextBatchIndex { get; private set; }

    public int RequestsIssued { get; set; }

    public int InProcessWaits { get; set; }

    public TimeSpan TotalWaited { get; set; }

    public DateTimeOffset LastRequestStartedAt { get; set; }

    /// <summary>
    /// Returns the current batch index and advances the run, so the publisher never holds a
    /// captured mutable counter.
    /// </summary>
    public int TakeNextBatchIndex()
    {
        int current = NextBatchIndex;
        NextBatchIndex++;
        return current;
    }
}
