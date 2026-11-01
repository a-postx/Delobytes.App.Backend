using Delobytes.App.Backend.Integrations.Application.Interfaces;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The Ozon FBO backfill branch: the same day-by-day walk Wildberries uses, but each day is read
/// through a cursor-paged reader, so a day holding more postings than one page is followed to its
/// last cursor instead of being treated as an unservable partial day.
/// </summary>
public sealed class OzonBackfillOrdersFetcher : IOrdersFetcher
{
    private readonly OrdersBackfillWalker _walker;
    private readonly IOrdersWindowReader _windowReader;

    public OzonBackfillOrdersFetcher(OrdersBackfillWalker walker, IOrdersWindowReader windowReader)
    {
        _walker = walker;
        _windowReader = windowReader;
    }

    /// <inheritdoc/>
    public async Task<FetchOutcome> FetchAsync(
        OrdersImportContext context, OrdersRunProgress progress, CancellationToken cancellationToken)
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
