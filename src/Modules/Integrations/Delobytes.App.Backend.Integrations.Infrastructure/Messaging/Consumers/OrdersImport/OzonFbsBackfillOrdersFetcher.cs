using Delobytes.App.Backend.Integrations.Application.Interfaces;

namespace Delobytes.App.Backend.Integrations.Infrastructure.Messaging.Consumers.OrdersImport;

/// <summary>
/// The Ozon FBS backfill branch: the same day-by-day walk as FBO, reading every day through
/// <see cref="OzonFbsOrdersReader"/>. A distinct type rather than a second registration of
/// <see cref="OzonBackfillOrdersFetcher"/>, which DI could not tell apart from the FBO one.
/// </summary>
public sealed class OzonFbsBackfillOrdersFetcher : IOrdersFetcher
{
    private readonly OrdersBackfillWalker _walker;
    private readonly IOrdersWindowReader _windowReader;

    public OzonFbsBackfillOrdersFetcher(OrdersBackfillWalker walker, OzonFbsOrdersReader windowReader)
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
