using Delobytes.App.Backend.Integrations.Application.Options;
using Microsoft.Extensions.Options;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// Wraps the existing Wildberries backfill/delta fetchers and <see cref="BisectingOrdersReader"/>
/// behind <see cref="IOrdersFetchers"/>, with tuning read from <see cref="WildberriesImportOptions"/>
/// and the shared request budget from <see cref="OrdersImportConstants"/>.
/// </summary>
public sealed class WildberriesOrdersFetchers : IOrdersFetchers
{
    public WildberriesOrdersFetchers(
        BackfillOrdersFetcher backfill,
        DeltaOrdersFetcher delta,
        BisectingOrdersReader windowReader,
        IOptions<WildberriesImportOptions> options)
    {
        WildberriesImportOptions importOptions = options.Value;

        Backfill = backfill;
        Delta = delta;
        WindowReader = windowReader;

        MaxInProcessWait = TimeSpan.FromSeconds(importOptions.MaxInProcessWaitSeconds >= 0
            ? importOptions.MaxInProcessWaitSeconds
            : WildberriesImportOptions.DefaultMaxInProcessWaitSeconds);

        MaxRequestsPerRun = OrdersImportConstants.MaxRequestsPerRun;

        RunBudget = TimeSpan.FromSeconds(importOptions.BackfillRunBudgetSeconds > 0
            ? importOptions.BackfillRunBudgetSeconds
            : WildberriesImportOptions.DefaultBackfillRunBudgetSeconds);
    }

    public IOrdersFetcher Backfill { get; }

    public IOrdersFetcher Delta { get; }

    public IOrdersWindowReader WindowReader { get; }

    public TimeSpan MaxInProcessWait { get; }

    public int MaxRequestsPerRun { get; }

    public TimeSpan RunBudget { get; }
}
