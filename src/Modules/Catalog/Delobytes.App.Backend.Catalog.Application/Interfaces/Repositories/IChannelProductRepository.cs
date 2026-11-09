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
}
