namespace Delobytes.App.Backend.Integrations.Application.Options;

/// <summary>
/// Operational configuration for WatchdogHostedService.
/// </summary>
public class WatchdogOptions
{
    /// <summary>
    /// How often WatchdogHostedService checks for Running jobs that exceeded their configured
    /// timeout, in minutes.
    /// </summary>
    public double IntervalMinutes { get; set; } = 5;

    /// <summary>
    /// A Pending run created longer ago than this, and not deferred to a future instant, is failed.
    /// Nothing marks a run as dispatched, so a consumer that fails before the run enters Running
    /// leaves the row Pending, where it is re-published every worker tick and blocks its schedule
    /// through HasActiveJobAsync forever. The value must exceed the longest per-type timeout so a
    /// job legitimately waiting behind a running one is not killed.
    /// </summary>
    public int PendingTimeoutMinutes { get; set; } = 120;
}
