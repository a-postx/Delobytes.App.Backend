namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Reads one run's orders. The backfill and delta implementations differ in what they read, and
/// publishing is their responsibility: a window reader only reads.
/// </summary>
public interface IOrdersFetcher
{
    /// <summary>
    /// Fetches the run's orders, publishing each unit as it is read. A recoverable stop (throttle,
    /// time budget, request budget) is returned in the outcome, not thrown.
    /// </summary>
    public Task<FetchOutcome> FetchAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        CancellationToken cancellationToken);
}
