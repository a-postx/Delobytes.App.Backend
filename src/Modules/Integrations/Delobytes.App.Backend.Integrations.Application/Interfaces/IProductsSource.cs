using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Channel-neutral capability for retrieving product cards. Implemented by API clients that
/// support product catalog sync, so a new job type touching only orders does not require
/// changes to <see cref="IChannelApiClient"/> across every marketplace.
/// </summary>
public interface IProductsSource
{
    /// <summary>
    /// Retrieves one page of product cards for the specified connection.
    /// </summary>
    /// <param name="connectionId">Identifier of the Connection to authenticate as.</param>
    /// <param name="request">Page request (cursor and limit).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<ProductCardsPage> GetProductCardsAsync(
        Guid connectionId, ProductCardsPageRequest request, CancellationToken cancellationToken);
}
