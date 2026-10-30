namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Reads one run's orders. The backfill and delta implementations differ in what they read so the
/// orchestrator only learns whether the run completed and, if not, when it can resume.
/// </summary>
public interface IOrdersFetcher
{
    /// <summary>
    /// Fetches the run's orders, publishing each unit as it is read. A recoverable stop (throttle,
    /// budget) is returned in the outcome, not thrown.
    /// </summary>
    public Task<FetchOutcome> FetchAsync(OrdersImportContext context, OrdersRunProgress progress, CancellationToken cancellationToken);
}
