namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The backfill fetcher, delta fetcher and run-level tuning one channel needs to run an orders
/// import job type end to end. Resolved by <see cref="IOrdersFetcherResolver"/> from the
/// connection's channel code and the <see cref="Domain.Enums.JobType"/> of the job run, so
/// <c>ProcessOrdersImportConsumer</c> stays channel-agnostic.
/// </summary>
public interface IOrdersFetchers
{
    public IOrdersFetcher Backfill { get; }

    public IOrdersFetcher Delta { get; }

    public IOrdersWindowReader WindowReader { get; }

    /// <summary>
    /// Gets the longest throttle a request may wait out in-process before reporting a stop instead.
    /// </summary>
    public TimeSpan MaxInProcessWait { get; }

    /// <summary>
    /// Gets the hard bound on the number of HTTP requests one run may issue.
    /// </summary>
    public int MaxRequestsPerRun { get; }

    /// <summary>
    /// Gets the wall-clock fetch budget for one run, used to compute the orchestrator's deadline.
    /// </summary>
    public TimeSpan RunBudget { get; }
}
