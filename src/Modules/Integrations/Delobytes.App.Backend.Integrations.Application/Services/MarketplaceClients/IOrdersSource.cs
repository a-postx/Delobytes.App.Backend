using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Services.MarketplaceClients;

/// <summary>
/// Channel-neutral capability for retrieving orders. Implemented by API clients that support
/// orders sync, kept separate from <see cref="IProductsSource"/> so adding orders import does
/// not force every marketplace client to implement an orders method it cannot support.
/// </summary>
public interface IOrdersSource
{
    /// <summary>
    /// Retrieves one page of orders for the specified connection and time window.
    /// </summary>
    /// <param name="connectionId">Identifier of the Connection to authenticate as.</param>
    /// <param name="request">Window, selection mode and limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<OrdersPage> GetOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken);
}
