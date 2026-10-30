using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Immutable result of one fetch, shared by the backfill walk and the delta bisection. A stop is a
/// value, not an exception: the caller decides whether it is recoverable (throttle, budget) or
/// terminal (a window the endpoint cannot serve whole).
/// </summary>
public sealed record FetchOutcome
{
    public required int RequestsIssued { get; init; }

    /// <summary>
    /// Gets the number of orders the fetch published or read. Backfill publishes per day and
    /// returns no orders, so this cannot be derived from <see cref="Orders"/>.
    /// </summary>
    public required int OrdersFetched { get; init; }

    public required StopReason StopReason { get; init; }

    public DateTimeOffset? ResumeAt { get; init; }

    public DateTimeOffset? MaxChangedAt { get; init; }

    /// <summary>
    /// Gets the watermark a backfill hands over to the delta path once its catch-up pass completes.
    /// Null on a delta fetch and on any backfill that has not completed, so neither can advance a
    /// watermark it has not earned.
    /// </summary>
    public DateTimeOffset? HandoverWatermark { get; init; }

    /// <summary>
    /// Gets a value indicating whether a backfill fetch finished the catch-up pass. Always false on
    /// a delta fetch.
    /// </summary>
    public bool IsBackfillCompleted { get; init; }

    /// <summary>
    /// Gets the orders read by a delta fetch. Always empty for backfill.
    /// </summary>
    public IReadOnlyList<ChannelOrderSnapshot> Orders { get; init; } = Array.Empty<ChannelOrderSnapshot>();

    /// <summary>
    /// Gets a value indicating whether the fetch was cut short and the run can resume later.
    /// </summary>
    public bool IsStopped
    {
        get
        {
            return StopReason == StopReason.Throttled || StopReason == StopReason.Budget;
        }
    }

    public static FetchOutcome Stopped(
        StopReason stopReason,
        DateTimeOffset? resumeAt,
        int requestsIssued)
    {
        return new FetchOutcome
        {
            RequestsIssued = requestsIssued,
            OrdersFetched = 0,
            StopReason = stopReason,
            ResumeAt = resumeAt,
        };
    }
}
