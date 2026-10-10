using Delobytes.App.Backend.Integrations.Application.DTOs;
using Delobytes.App.Backend.Integrations.Application.Models;

namespace Delobytes.App.Backend.Integrations.Application.Interfaces;

/// <summary>
/// Defines the contract for a selling channel API client.
/// </summary>
public interface IChannelApiClient
{
    /// <summary>
    /// Gets the code of the channel this instance serves, matching the system channel template
    /// code the factory bound it to. Bound by the factory, never by the caller.
    /// </summary>
    public string ChannelCode { get; }

    /// <summary>
    /// Retrieves account/shop information for the supplied credentials.
    /// Returns null when the marketplace does not expose this information or the call fails.
    /// </summary>
    /// <param name="apiKey">API key.</param>
    /// <param name="apiSecret">Optional API secret.</param>
    /// <param name="settings">Optional extra settings (e.g. sellerId for Ozon).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<AccountInfo?> GetAccountInfoAsync(
        string apiKey,
        string? apiSecret,
        Dictionary<string, string>? settings,
        CancellationToken ct);

    /// <summary>
    /// Retrieves a single page of orders. Paging is part of the signature because no channel
    /// returns a whole period in one call.
    /// </summary>
    /// <param name="request">Page request: period, cursor, page size and optional prefilter.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>API response containing one page of normalized orders.</returns>
    public Task<ApiResponse<OrdersPage>> GetOrdersPageAsync(OrdersPageRequest request, CancellationToken ct);

    /// <summary>
    /// Retrieves one page of product cards using cursor-based pagination.
    /// </summary>
    /// <param name="cursor">Cursor for pagination. Null for the first page.</param>
    /// <param name="limit">Maximum number of cards to retrieve (1-100).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>API response containing product cards data with pagination cursor.</returns>
    public Task<ApiResponse<ProductCardsData>> GetProductCardsAsync(ProductCardsCursor? cursor, int limit, CancellationToken ct);
}
