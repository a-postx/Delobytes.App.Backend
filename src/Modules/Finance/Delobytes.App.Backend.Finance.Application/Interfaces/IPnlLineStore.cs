using Delobytes.App.Backend.Finance.Domain.Entities;

namespace Delobytes.App.Backend.Finance.Application.Interfaces;

/// <summary>
/// Persistence port of the projected P&amp;L lines.
/// </summary>
public interface IPnlLineStore
{
    /// <summary>
    /// Loads the P&amp;L lines of <paramref name="orderLineIds"/>.
    /// </summary>
    /// <param name="orderLineIds">Order line identifiers.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The lines found.</returns>
    public Task<IReadOnlyList<PnlLine>> LoadAsync(
        IReadOnlyCollection<Guid> orderLineIds,
        CancellationToken cancellationToken);

    /// <summary>
    /// Stages a new P&amp;L line.
    /// </summary>
    /// <param name="line">Line to add.</param>
    public void Add(PnlLine line);

    /// <summary>
    /// Loads the order line identifiers of the lines whose period date falls in
    /// [<paramref name="from"/>, <paramref name="to"/>], for a rebuild of a period. The period date
    /// is the sale date when the line sold and the first day of its financial period otherwise, so
    /// a line that never sold is rebuilt as well. Lines with neither date are included so a rebuild
    /// repairs rows written before the period attribution existed.
    /// </summary>
    /// <param name="from">First period date, inclusive.</param>
    /// <param name="to">Last period date, inclusive.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The order line identifiers.</returns>
    public Task<IReadOnlyList<Guid>> LoadOrderLineIdsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists every pending change.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SaveChangesAsync(CancellationToken cancellationToken);
}
