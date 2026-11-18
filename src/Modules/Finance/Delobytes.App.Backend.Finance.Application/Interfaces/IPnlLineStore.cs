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
    /// Persists every pending change.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task SaveChangesAsync(CancellationToken cancellationToken);
}
