using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The backfill branch: one calendar day per request, walking backwards, then a catch-up pass. A
/// day the endpoint cannot serve whole is fatal: the window reader has no smaller unit to bisect
/// a single flag=1 day to.
/// </summary>
public sealed class BackfillOrdersFetcher : IOrdersFetcher
{
    private readonly OrdersBackfillWalker _walker;
    private readonly IOrdersWindowReader _windowReader;
    public BackfillOrdersFetcher(OrdersBackfillWalker walker, IOrdersWindowReader windowReader)
    {
        _walker = walker;
        _windowReader = windowReader;
    }

    public async Task<FetchOutcome> FetchAsync(OrdersImportContext context, OrdersRunProgress progress, CancellationToken cancellationToken)
    {
        FetchOutcome outcome = await _walker.FetchBackfillDaysAsync(
            context, progress, _windowReader, cancellationToken);

        // The catch-up pass is what completes the cursor and stamps the handover watermark, so both
        // have to be read off the cursor after the walk rather than before it.
        return outcome with
        {
            IsBackfillCompleted = context.BackfillState.IsCompleted,
            HandoverWatermark = context.BackfillState.IsCompleted ? context.BackfillState.HandoverWatermark : null,
        };
    }
}
