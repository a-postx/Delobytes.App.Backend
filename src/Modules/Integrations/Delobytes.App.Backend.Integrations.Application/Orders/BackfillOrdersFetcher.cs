using Delobytes.App.Backend.Integrations.Application.Interfaces;
using Delobytes.App.Backend.Integrations.Domain.Entities;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

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

    public async Task<FetchOutcome> FetchAsync(
        OrdersImportContext context,
        RunCounters counters,
        BatchSequence batches,
        CancellationToken cancellationToken)
    {
        return await _walker.FetchBackfillDaysAsync(
            context, counters, batches, _windowReader, cancellationToken);
    }
}
