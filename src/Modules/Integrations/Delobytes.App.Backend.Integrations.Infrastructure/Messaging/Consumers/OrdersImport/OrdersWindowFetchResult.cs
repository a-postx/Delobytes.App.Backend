using Delobytes.App.Backend.Integrations.Contracts.Models;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Outcome of fetching one window. A stop is a value, not an exception: the caller decides whether
/// it is recoverable (throttle, budget) or terminal (a window the endpoint cannot serve whole).
/// </summary>
public sealed record OrdersWindowFetchResult
{
    public required List<ChannelOrderSnapshot> Orders { get; init; }

    public required int RequestsIssued { get; init; }

    public required int RowsWithoutIdentity { get; init; }

    public required StopReason StopReason { get; init; }

    /// <summary>
    /// Gets the instant the source will accept the next request, when it stopped because of a
    /// throttle or the run's budget.
    /// </summary>
    public DateTimeOffset? ResumeAt { get; init; }

    public DateTimeOffset? MaxChangedAt { get; init; }

    /// <summary>
    /// Gets a value indicating whether the window was not fully read and the run can resume later.
    /// </summary>
    public bool IsStopped => StopReason == StopReason.Throttled || StopReason == StopReason.Budget;

    public static OrdersWindowFetchResult Stopped(
        StopReason stopReason,
        DateTimeOffset? resumeAt,
        int requestsIssued,
        int rowsWithoutIdentity)
    {
        return new OrdersWindowFetchResult
        {
            Orders = new List<ChannelOrderSnapshot>(),
            RequestsIssued = requestsIssued,
            RowsWithoutIdentity = rowsWithoutIdentity,
            StopReason = stopReason,
            ResumeAt = resumeAt,
        };
    }
}
