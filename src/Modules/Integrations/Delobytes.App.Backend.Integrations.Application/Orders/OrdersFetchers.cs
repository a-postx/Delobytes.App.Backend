using Delobytes.App.Backend.Integrations.Application.Options;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// The single fetcher set a (channel code, orders job type) pair runs. One instance per pair, so
/// the container can tell a backfill fetcher, a delta fetcher and their tuning apart without a
/// concrete wrapper class per channel.
/// </summary>
public sealed class OrdersFetchers : IOrdersFetchers
{
    public OrdersFetchers(IOrdersFetcher backfill, IOrdersFetcher delta, OrdersImportTuning tuning)
    {
        Backfill = backfill;
        Delta = delta;
        Tuning = tuning;
    }

    public IOrdersFetcher Backfill { get; }

    public IOrdersFetcher Delta { get; }

    public OrdersImportTuning Tuning { get; }

    public int MaxRequestsPerRun
    {
        get
        {
            return Tuning.MaxRequestsPerRun;
        }
    }

    public TimeSpan RunBudget
    {
        get
        {
            return Tuning.RunBudget;
        }
    }
}
