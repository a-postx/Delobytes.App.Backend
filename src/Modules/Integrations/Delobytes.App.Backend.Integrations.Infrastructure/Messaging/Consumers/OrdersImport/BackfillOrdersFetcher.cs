using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The backfill branch: one calendar day per request, walking backwards, then a catch-up pass. A
/// day the endpoint cannot serve whole is fatal, because flag=1 has no smaller unit to bisect to.
/// </summary>
public sealed class BackfillOrdersFetcher : IOrdersFetcher
{
    private readonly OrdersBackfillWalker _walker;

    public BackfillOrdersFetcher(OrdersBackfillWalker walker)
    {
        _walker = walker;
    }

    public async Task<FetchOutcome> FetchAsync(OrdersImportRun run, CancellationToken cancellationToken)
    {
        IOrdersSource ordersSource = run.OrdersSource
            ?? throw new InvalidOperationException("Backfill fetch requires a resolved orders source.");
        OrdersBackfillState backfillState = run.BackfillState
            ?? throw new InvalidOperationException("Backfill fetch requires the connection's backfill cursor.");

        FetchOutcome outcome = await _walker.FetchBackfillDaysAsync(
            ordersSource, run, backfillState, cancellationToken);

        // The catch-up pass is what completes the cursor and stamps the handover watermark, so both
        // have to be read off the cursor after the walk rather than before it.
        return outcome with
        {
            IsBackfillCompleted = backfillState.IsCompleted,
            HandoverWatermark = backfillState.IsCompleted ? backfillState.HandoverWatermark : null,
        };
    }
}
