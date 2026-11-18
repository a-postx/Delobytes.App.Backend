using Delobytes.App.Backend.Catalog.Domain.Entities;

namespace Delobytes.App.Backend.Catalog.Application.Interfaces.Repositories;

/// <summary>
/// Repository for the channel-product links of the catalog.
/// </summary>
public interface IChannelProductRepository
{
    /// <summary>
    /// Returns the active links of the channel whose last sync predates
    /// <paramref name="syncedBefore"/>. A link without a sync stamp has never been observed by an
    /// import, so it is not returned.
    /// </summary>
    /// <param name="channelId">Channel whose links are inspected.</param>
    /// <param name="syncedBefore">Boundary the links must have been synced before.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<ChannelProduct>> GetAbandonedLinksAsync(
        Guid channelId,
        DateTimeOffset syncedBefore,
        CancellationToken cancellationToken);

    /// <summary>Persists every pending change.</summary>
    public Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Resolves the channel product of a marketplace item by its natural key
    /// <c>(ChannelId, ExternalProductId)</c>. Not filtered by <c>IsActive</c>: an archived card still
    /// owns historical order lines, and refusing to resolve it would lose their cost.
    /// </summary>
    /// <param name="channelId">Channel identifier.</param>
    /// <param name="externalProductId">Channel-side product identity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The channel product identifier, or null when the item is unknown.</returns>
    public Task<Guid?> FindChannelProductIdAsync(
        Guid channelId,
        string externalProductId,
        CancellationToken cancellationToken);
}
