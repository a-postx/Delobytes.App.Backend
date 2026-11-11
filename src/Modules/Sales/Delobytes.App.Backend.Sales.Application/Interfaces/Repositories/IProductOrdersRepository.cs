namespace Delobytes.App.Backend.Sales.Application.Interfaces.Repositories;

/// <summary>
/// Read-side repository used to check whether any orders reference the given channel products
/// before a product is deleted in Catalog.
/// </summary>
public interface IProductOrdersRepository
{
    /// <summary>
    /// Counts distinct orders that have at least one line pointing at one of the given channel
    /// products.
    /// </summary>
    /// <param name="channelProductIds">Channel product ids to look for.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of distinct orders referencing the channel products.</returns>
    public Task<int> CountOrdersForChannelProductsAsync(
        IReadOnlyCollection<Guid> channelProductIds,
        CancellationToken cancellationToken);
}
