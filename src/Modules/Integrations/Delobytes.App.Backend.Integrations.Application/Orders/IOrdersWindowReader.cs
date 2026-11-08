using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Orders;

/// <summary>
/// Reads one orders window to completion, however the channel paginates it (date bisection,
/// opaque cursor, ...). A window is either read completely - every page merged into a
/// <see cref="FetchOutcome.DeltaRead"/> with no stop - or not read at all: a stop is returned and
/// the caller must publish nothing, since anything already read belongs to a window the reader
/// never finished.
/// </summary>
public interface IOrdersWindowReader
{
    /// <summary>
    /// Reads the requested window to completion.
    /// </summary>
    /// <param name="request">The window to read, including the starting cursor where the channel
    /// paginates by one.</param>
    /// <param name="context">The run's connection, deadline and orders source.</param>
    /// <param name="counters">The run's shared request and pacing counters.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The merged window when it was read whole, or a stop when the deadline, the budget or a
    /// throttle cut it short. A stop carries no orders: the window stays unread, so the caller must
    /// publish nothing from it.
    /// </returns>
    public Task<FetchOutcome> ReadWindowAsync(
        OrdersRequest request,
        OrdersImportContext context,
        RunCounters counters,
        CancellationToken cancellationToken);
}
