using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Mutable progress of one orders import run: the window it reads, the batch index it will assign
/// next, and the budget and stop accounting the fetch helpers record as they work.
/// </summary>
public sealed class OrdersImportRun
{
    public OrdersImportRun(
        JobRun jobRun,
        Connection connection,
        DateTimeOffset deadline,
        DateTimeOffset windowTo,
        bool isBackfill,
        int nextBatchIndex)
    {
        JobRun = jobRun;
        Connection = connection;
        Deadline = deadline;
        WindowTo = windowTo;
        IsBackfill = isBackfill;
        NextBatchIndex = nextBatchIndex;
        StopReason = StopReason.Completed;
    }

    public JobRun JobRun { get; }

    public Connection Connection { get; }

    public DateTimeOffset Deadline { get; }

    public DateTimeOffset WindowTo { get; }

    public bool IsBackfill { get; }

    public int NextBatchIndex { get; private set; }

    public int RequestsIssued { get; set; }

    public int RowsWithoutIdentity { get; set; }

    public int InProcessWaits { get; set; }

    public TimeSpan TotalWaited { get; set; }

    public StopReason StopReason { get; set; }

    /// <summary>
    /// Gets or sets the instant the source will accept the next request, set only when the run
    /// stopped because of a throttle or the run budget.
    /// </summary>
    public DateTimeOffset? ResumeAt { get; set; }

    public DateTimeOffset LastRequestStartedAt { get; set; }

    /// <summary>
    /// Gets or sets the channel capability the fetcher reads from. The fetcher contract carries
    /// only the run, and resolving the capability is what decides whether a run may start at all, so
    /// the orchestrator resolves it once, fails the job when it is missing, and hands it over here.
    /// </summary>
    public IOrdersSource? OrdersSource { get; set; }

    /// <summary>
    /// Gets or sets the per-connection backfill cursor. The backfill fetcher advances it, the delta
    /// fetcher reads its handover watermark, and its completion state is what decides which of the
    /// two fetches this run is.
    /// </summary>
    public OrdersBackfillState? BackfillState { get; set; }

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
