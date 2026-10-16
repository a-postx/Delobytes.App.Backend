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
    /// <remarks>
    /// The returned page never substitutes a fabricated identity for a row the source cannot
    /// identify. Such rows are dropped and counted in
    /// <see cref="Models.OrdersPage.RowsWithoutIdentity"/> instead, and the caller is expected to
    /// treat a non-zero count as an incomplete fetch rather than a success.
    /// </remarks>
    public Task<OrdersPage> GetOrdersAsync(
        Guid connectionId, OrdersRequest request, CancellationToken cancellationToken);
}
