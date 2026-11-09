using Delobytes.App.Backend.Integrations.Application.Options;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// The backfill fetcher, delta fetcher and run-level tuning one channel needs to run an orders
/// import job type end to end. Resolved by <see cref="IOrdersFetcherResolver"/> from the
/// connection's channel code and the <see cref="Domain.Enums.JobType"/> of the job run, so
/// <c>OrdersImportOrchestrator</c> stays channel-agnostic.
/// </summary>
public interface IOrdersFetchers
{
    public IOrdersFetcher Backfill { get; }

    public IOrdersFetcher Delta { get; }

    /// <summary>
    /// Gets the resolved settings for this (channel, job type): every value is already effective.
    /// </summary>
    public OrdersImportTuning Tuning { get; }

    /// <summary>
    /// Gets the hard bound on the number of HTTP requests one run may issue.
    /// </summary>
    public int MaxRequestsPerRun { get; }

    /// <summary>
    /// Gets the wall-clock fetch budget for one run, used to compute the orchestrator's deadline.
    /// </summary>
    public TimeSpan RunBudget { get; }
}
