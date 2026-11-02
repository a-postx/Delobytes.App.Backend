using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Wraps the Ozon FBS backfill/delta fetchers behind <see cref="IOrdersFetchers"/>, with tuning
/// read from <see cref="OzonImportOptions"/>. Separate from <see cref="OzonOrdersFetchers"/> so the
/// resolver can hand a job type exactly the schema it must read.
/// </summary>
public sealed class OzonFbsOrdersFetchers : IOrdersFetchers
{
    public OzonFbsOrdersFetchers(
        OzonFbsBackfillOrdersFetcher backfill,
        OzonFbsDeltaOrdersFetcher delta,
        OzonFbsOrdersReader windowReader,
        IOptions<OzonImportOptions> options)
    {
        OzonImportOptions importOptions = options.Value;

        Backfill = backfill;
        Delta = delta;
        WindowReader = windowReader;

        MaxInProcessWait = TimeSpan.FromSeconds(importOptions.MaxInProcessWaitSeconds >= 0
            ? importOptions.MaxInProcessWaitSeconds
            : OzonImportOptions.DefaultMaxInProcessWaitSeconds);

        MaxRequestsPerRun = importOptions.MaxRequestsPerRun > 0
            ? importOptions.MaxRequestsPerRun
            : OzonImportOptions.DefaultMaxRequestsPerRun;

        RunBudget = TimeSpan.FromSeconds(importOptions.BackfillRunBudgetSeconds > 0
            ? importOptions.BackfillRunBudgetSeconds
            : OzonImportOptions.DefaultBackfillRunBudgetSeconds);
    }

    public IOrdersFetcher Backfill { get; }

    public IOrdersFetcher Delta { get; }

    public IOrdersWindowReader WindowReader { get; }

    public TimeSpan MaxInProcessWait { get; }

    public int MaxRequestsPerRun { get; }

    public TimeSpan RunBudget { get; }
}
