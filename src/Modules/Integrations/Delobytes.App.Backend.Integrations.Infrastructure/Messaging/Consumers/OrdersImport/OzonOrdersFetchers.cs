using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Wraps the Ozon FBO backfill/delta fetchers behind <see cref="IOrdersFetchers"/>, with tuning
/// read from <see cref="OzonImportOptions"/>.
/// </summary>
public sealed class OzonOrdersFetchers : IOrdersFetchers
{
    public OzonOrdersFetchers(
        OzonBackfillOrdersFetcher backfill,
        OzonDeltaOrdersFetcher delta,
        IOrdersWindowReader windowReader,
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
